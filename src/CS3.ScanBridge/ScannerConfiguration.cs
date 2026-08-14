using NAPS2.Images;
using NAPS2.Scan;
using NTwain.Data;

namespace CS3.ScanBridge;

internal static class ScannerConfiguration
{
    public static ScanDevice? SelectDevice(AppSettings settings, IEnumerable<ScanDevice> devices) =>
        devices.FirstOrDefault(device =>
            !string.IsNullOrWhiteSpace(settings.ScannerDeviceId) &&
            string.Equals(device.ID, settings.ScannerDeviceId, StringComparison.Ordinal)) ??
        devices.FirstOrDefault(device =>
            !string.IsNullOrWhiteSpace(settings.ScannerName) &&
            string.Equals(device.Name, settings.ScannerName, StringComparison.Ordinal));

    public static BitDepth GetBitDepth(ScanColourMode colourMode) => colourMode switch
    {
        ScanColourMode.Colour => BitDepth.Color,
        ScanColourMode.Greyscale => BitDepth.Grayscale,
        _ => BitDepth.BlackAndWhite
    };

    public static ScanOptions CreateTwainOptions(AppSettings settings, ScanDevice device) => new()
    {
        Driver = Driver.Twain,
        Device = device,
        Dpi = settings.Dpi,
        BitDepth = GetBitDepth(settings.ColourMode),
        PaperSource = settings.Duplex ? PaperSource.Duplex : PaperSource.Feeder,
        PageSize = PageSize.A4,
        PageAlign = HorizontalAlign.Right,
        BrightnessContrastAfterScan = true,
        Quality = settings.JpegQuality,
        TwainOptions = new TwainOptions
        {
            Dsm = TwainDsm.New,
            TransferMode = TwainTransferMode.Memory,
            ShowProgress = false
        }
    };

    public static ScanOptions CreateWiaOptions(
        AppSettings settings,
        ScanDevice device,
        WiaDocumentSourceSelection source)
    {
        var options = new ScanOptions
        {
            Driver = Driver.Wia,
            Device = device,
            Dpi = settings.Dpi,
            BitDepth = GetBitDepth(settings.ColourMode),
            PaperSource = source.UsesFeeder
                ? settings.Duplex ? PaperSource.Duplex : PaperSource.Feeder
                : PaperSource.Flatbed,
            Quality = settings.JpegQuality,
            WiaOptions = new WiaOptions { WiaApiVersion = WiaApiVersion.Wia20 }
        };
        if (settings.PaperSize == ScanPaperSize.A4) options.PageSize = PageSize.A4;
        return options;
    }
}

internal static class ScannerFailureMessage
{
    public static string? DescribeKnown(string provider, Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = current.GetType().Name switch
            {
                "DeviceNotFoundException" => $"The configured {provider} scanner is no longer registered.",
                "DeviceOfflineException" => $"The {provider} scanner is offline or disconnected.",
                "DeviceCommunicationException" => $"Communication with the {provider} scanner failed.",
                "DevicePaperJamException" => $"The {provider} scanner reports a paper jam.",
                "DeviceBusyException" => $"Another program is using the {provider} scanner.",
                "DeviceException" => $"The {provider} driver reported an error: {current.Message}",
                _ => null
            };
            if (message is not null) return message;
        }
        return null;
    }
}
