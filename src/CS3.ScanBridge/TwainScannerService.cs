using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Scan;
using NTwain.Data;

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
        var device = new ScanDevice(
            Driver.Twain,
            settings.ScannerDeviceId ?? settings.ScannerName ?? string.Empty,
            settings.ScannerName ?? settings.ScannerDeviceId ?? string.Empty);
        var options = new ScanOptions
        {
            Driver = Driver.Twain,
            Device = device,
            Dpi = settings.Dpi,
            BitDepth = settings.ColourMode switch
            {
                ScanColourMode.Colour => BitDepth.Color,
                ScanColourMode.Greyscale => BitDepth.Grayscale,
                _ => BitDepth.BlackAndWhite
            },
            PaperSource = settings.Duplex ? PaperSource.Duplex : PaperSource.Feeder,
            // The DS-740D has an A4-width transport. NAPS2 requires an explicit page size.
            PageSize = PageSize.A4,
            // Match the proven NAPS2 DS-740D profile. Left alignment offsets A4 inside the
            // 8.5-inch transport and this Brother driver can reject that image layout.
            PageAlign = HorizontalAlign.Right,
            // Do not send brightness and contrast controls to the driver. The saved NAPS2
            // profile also applies these after acquisition.
            BrightnessContrastAfterScan = true,
            Quality = settings.JpegQuality,
            TwainOptions = new TwainOptions
            {
                Dsm = TwainDsm.New,
                TransferMode = TwainTransferMode.Memory,
                ShowProgress = false
            }
        };

        var pages = new List<ScanPage>();
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
                        using var output = new MemoryStream();
                        image.Save(output, ImageFileFormat.Jpeg, new ImageSaveOptions { Quality = settings.JpegQuality });
                        pages.Add(new ScanPage(output.ToArray(), "jpeg"));
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
        catch (Exception exception)
        {
            logger.LogWarning(exception, "NAPS2 worker failed to scan with TWAIN source {SourceId}", device.ID);
            throw new ScannerUnavailableException(DescribeWorkerFailure(exception));
        }

        if (pages.Count == 0) throw new NoPagesException("No document or pages were acquired.");
        return new ScanAcquisition(pages);
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

    private static string DescribeWorkerFailure(Exception exception) => exception.GetType().Name switch
    {
        "DeviceNotFoundException" => "The configured TWAIN scanner is no longer registered.",
        "DeviceOfflineException" => "The TWAIN scanner is offline or disconnected.",
        "DeviceCommunicationException" => "Communication with the TWAIN scanner failed.",
        "DevicePaperJamException" => "The TWAIN scanner reports a paper jam.",
        "DeviceBusyException" => "Another program is using the TWAIN scanner.",
        "DeviceException" => $"The TWAIN driver reported an error: {exception.Message}",
        _ => $"The NAPS2 TWAIN worker failed: {exception.Message}"
    };
}

internal static class TwainFailureMessage
{
    public static string Describe(string operation, ReturnCode returnCode, ConditionCode? conditionCode)
    {
        var action = (returnCode, conditionCode) switch
        {
            (ReturnCode.Busy or ReturnCode.ScannerLocked, _) or (_, ConditionCode.MaxConnections) =>
                "Another program is using the scanner. Close NAPS2 and Brother scanning software, then try again.",
            (_, ConditionCode.CheckDeviceOnline) =>
                "The scanner is offline. Connect its USB cable, turn it on, and wait for Windows to detect it.",
            (_, ConditionCode.NoDS) =>
                "The TWAIN driver is unavailable. Repair or reinstall the Brother scanner package.",
            (_, ConditionCode.NoMedia) =>
                "No document is loaded in the scanner.",
            (_, ConditionCode.PaperJam) =>
                "The scanner reports a paper jam.",
            (_, ConditionCode.PaperDoubleFeed) =>
                "The scanner reports a double feed.",
            (_, ConditionCode.SeqError) =>
                "The TWAIN driver is in an invalid state. Close other scanning programs and restart CS3 Scan Bridge.",
            (_, ConditionCode.Denied) =>
                "The scanner denied access. Close other scanning programs and try again.",
            _ =>
                "Confirm that the scanner is connected, turned on, and not open in another scanning program."
        };

        var status = conditionCode is null
            ? $"return code: {returnCode}"
            : $"return code: {returnCode}; condition code: {conditionCode}";
        return $"TWAIN could not {operation}. {action} ({status})";
    }
}
