using System.Runtime.InteropServices;
using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Scan;

namespace CS3.ScanBridge;

public sealed class WiaScannerService(ILogger<WiaScannerService> logger) : IScannerBackend, IDisposable
{
    private readonly StaWorker worker = new();

    public ScannerProvider Provider => ScannerProvider.Wia;

    public async Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken)
    {
        using var context = CreateContext();
        var controller = new ScanController(context);
        var scanners = new List<ScannerInfo>();
        await foreach (var device in controller.GetDevices(CreateDiscoveryOptions(), cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            scanners.Add(new ScannerInfo(device.ID, device.Name, ScannerProvider.Wia));
        }
        return scanners;
    }

    public async Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using var context = CreateContext();
        var controller = new ScanController(context);
        ScanDevice device;
        try
        {
            var devices = await GetDevicesAsync(controller, cancellationToken);
            device = ScannerConfiguration.SelectDevice(settings, devices) ??
                     throw new ScannerUnavailableException("The configured WIA scanner is no longer registered.");
        }
        catch (Exception exception) when (ScannerFailureMessage.DescribeKnown("WIA", exception) is not null)
        {
            throw new ScannerUnavailableException(ScannerFailureMessage.DescribeKnown("WIA", exception)!);
        }

        var documentHandlingStatus = await ReadDocumentHandlingStatusAsync(device.Name, cancellationToken);
        var source = WiaDocumentSourceSelector.Select(documentHandlingStatus, settings.Duplex);
        var options = ScannerConfiguration.CreateWiaOptions(settings, device, source);

        logger.LogInformation("NAPS2 WIA 2 selected {DocumentSource} from document handling status {DocumentHandlingStatus}",
            source.Description, documentHandlingStatus);

        var pages = new ScanPageBuffer();
        using var pageLimitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppedAtPageLimit = false;
        try
        {
            await foreach (var image in controller.Scan(options, pageLimitCancellation.Token)
                               .WithCancellation(pageLimitCancellation.Token))
            {
                using (image)
                {
                    if (pages.Count < settings.MaximumPages)
                    {
                        using var output = new SizeLimitedMemoryStream(pages.RemainingBytes);
                        image.Save(output, ImageFileFormat.Jpeg,
                            new ImageSaveOptions { Quality = settings.JpegQuality });
                        pages.Add(output.ToArray(), "jpeg");
                    }
                }

                if (pages.Count >= settings.MaximumPages)
                {
                    stoppedAtPageLimit = true;
                    pageLimitCancellation.Cancel();
                }
            }
        }
        catch (OperationCanceledException) when (stoppedAtPageLimit && !cancellationToken.IsCancellationRequested)
        {
            // The configured limit intentionally stops acquisition after the last accepted page.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (ScannerFailureMessage.DescribeKnown("WIA", exception) is not null)
        {
            logger.LogWarning(exception, "NAPS2 WIA 2 failed to scan source {SourceId}", device.ID);
            throw new ScannerUnavailableException(ScannerFailureMessage.DescribeKnown("WIA", exception)!);
        }

        if (pages.Count == 0) throw new NoPagesException("No document or pages were acquired.");
        return new ScanAcquisition(pages.Pages);
    }

    private ScanningContext CreateContext() => new(new GdiImageContext()) { Logger = logger };

    private static ScanOptions CreateDiscoveryOptions() => new()
    {
        Driver = Driver.Wia,
        WiaOptions = new WiaOptions { WiaApiVersion = WiaApiVersion.Wia20 }
    };

    private static async Task<IReadOnlyList<ScanDevice>> GetDevicesAsync(
        ScanController controller,
        CancellationToken cancellationToken)
    {
        var devices = new List<ScanDevice>();
        await foreach (var device in controller.GetDevices(CreateDiscoveryOptions(), cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            devices.Add(device);
        }
        return devices;
    }

    private async Task<int?> ReadDocumentHandlingStatusAsync(string scannerName, CancellationToken cancellationToken)
    {
        try
        {
            return await worker.InvokeAsync(() => ReadDocumentHandlingStatus(scannerName), cancellationToken);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidComObjectException)
        {
            logger.LogInformation(exception, "WIA document handling status is unavailable; ADF will be preferred");
            return null;
        }
    }

    private static int? ReadDocumentHandlingStatus(string scannerName)
    {
        dynamic? manager = null;
        dynamic? infos = null;
        try
        {
            manager = CreateComObject("WIA.DeviceManager");
            infos = manager.DeviceInfos;
            var count = (int)infos.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic? info = null;
                dynamic? device = null;
                try
                {
                    info = infos[index];
                    if ((int)info.Type != WiaConstants.ScannerDeviceType) continue;
                    if (!string.Equals(ReadPropertyAsString(info.Properties, WiaConstants.DeviceName), scannerName,
                            StringComparison.Ordinal)) continue;
                    device = info.Connect();
                    return ReadPropertyAsInt(device.Properties, WiaConstants.DocumentHandlingStatus);
                }
                finally
                {
                    ReleaseCom(device);
                    ReleaseCom(info);
                }
            }
            return null;
        }
        finally
        {
            ReleaseCom(infos);
            ReleaseCom(manager);
        }
    }

    private static string? ReadPropertyAsString(dynamic properties, int propertyId)
    {
        try
        {
            var count = (int)properties.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic? property = null;
                try
                {
                    property = properties[index];
                    if ((int)property.PropertyID == propertyId) return Convert.ToString(property.Value);
                }
                finally { ReleaseCom(property); }
            }
            return null;
        }
        finally { ReleaseCom(properties); }
    }

    private static int? ReadPropertyAsInt(dynamic properties, int propertyId)
    {
        try
        {
            var count = (int)properties.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic? property = null;
                try
                {
                    property = properties[index];
                    if ((int)property.PropertyID == propertyId) return Convert.ToInt32(property.Value);
                }
                finally { ReleaseCom(property); }
            }
            return null;
        }
        finally { ReleaseCom(properties); }
    }

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId) ??
                   throw new PlatformNotSupportedException("Windows Image Acquisition is unavailable.");
        return Activator.CreateInstance(type) ??
               throw new InvalidOperationException("Windows Image Acquisition could not be started.");
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
    }

    public void Dispose() => worker.Dispose();
}
