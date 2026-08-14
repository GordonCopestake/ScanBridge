using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Scan;

namespace CS3.ScanBridge;

public sealed class TwainScannerService(ILogger<TwainScannerService> logger) : IScannerBackend
{
    public ScannerProvider Provider => ScannerProvider.Twain;

    public async Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken)
    {
        using var context = CreateContext();
        var controller = new ScanController(context);
        var scanners = new List<ScannerInfo>();
        await foreach (var device in controller.GetDevices(CreateDiscoveryOptions(), cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            scanners.Add(new ScannerInfo(device.ID, device.Name, ScannerProvider.Twain));
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
                     throw new ScannerUnavailableException("The configured TWAIN scanner is no longer registered.");
        }
        catch (Exception exception) when (ScannerFailureMessage.DescribeKnown("TWAIN", exception) is not null)
        {
            throw new ScannerUnavailableException(ScannerFailureMessage.DescribeKnown("TWAIN", exception)!);
        }
        var options = ScannerConfiguration.CreateTwainOptions(settings, device);

        var pages = new ScanPageBuffer();
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppedAtPageLimit = false;
        try
        {
            await foreach (var image in controller.Scan(options, workerCancellation.Token)
                               .WithCancellation(workerCancellation.Token))
            {
                using (image)
                {
                    if (pages.Count < settings.MaximumPages)
                    {
                        using var output = new SizeLimitedMemoryStream(pages.RemainingBytes);
                        image.Save(output, ImageFileFormat.Jpeg, new ImageSaveOptions { Quality = settings.JpegQuality });
                        pages.Add(output.ToArray(), "jpeg");
                    }
                }

                if (pages.Count >= settings.MaximumPages)
                {
                    stoppedAtPageLimit = true;
                    workerCancellation.Cancel();
                }
            }
        }
        catch (OperationCanceledException) when (stoppedAtPageLimit && !cancellationToken.IsCancellationRequested)
        {
            // The page limit intentionally stops the worker after the last accepted page.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (ScannerFailureMessage.DescribeKnown("TWAIN", exception) is not null)
        {
            logger.LogWarning(exception, "NAPS2 worker failed to scan with TWAIN source {SourceId}", device.ID);
            throw new ScannerUnavailableException(ScannerFailureMessage.DescribeKnown("TWAIN", exception)!);
        }

        if (pages.Count == 0) throw new NoPagesException("No document or pages were acquired.");
        return new ScanAcquisition(pages.Pages);
    }

    private ScanningContext CreateContext()
    {
        var context = new ScanningContext(new GdiImageContext()) { Logger = logger };
        context.SetUpWin32Worker();
        return context;
    }

    private static ScanOptions CreateDiscoveryOptions() => new()
    {
        Driver = Driver.Twain,
        TwainOptions = new TwainOptions { Dsm = TwainDsm.New }
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
}
