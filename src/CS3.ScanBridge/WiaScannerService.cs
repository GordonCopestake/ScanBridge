using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CS3.ScanBridge;

public sealed class WiaScannerService : IScannerBackend, IDisposable
{
    private const int WiaErrorPaperEmpty = unchecked((int)0x80210003);
    private const int WiaErrorDeviceCommunication = unchecked((int)0x8021000A);
    private readonly StaWorker worker;
    private readonly ILogger<WiaScannerService> logger;

    public WiaScannerService(ILogger<WiaScannerService> logger)
    {
        this.logger = logger;
        worker = new StaWorker();
    }

    public ScannerProvider Provider => ScannerProvider.Wia;

    public Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken) =>
        worker.InvokeAsync<IReadOnlyList<ScannerInfo>>(EnumerateScanners, cancellationToken);

    public Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken) =>
        worker.InvokeAsync(() => Acquire(settings), cancellationToken);

    private static IReadOnlyList<ScannerInfo> EnumerateScanners()
    {
        var result = new List<ScannerInfo>();
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
                try
                {
                    info = infos[index];
                    if ((int)info.Type != WiaConstants.ScannerDeviceType) continue;
                    var id = ReadPropertyAsString(info.Properties, WiaConstants.DeviceId);
                    var name = ReadPropertyAsString(info.Properties, WiaConstants.DeviceName);
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) result.Add(new(id, name));
                }
                finally { ReleaseCom(info); }
            }
        }
        finally
        {
            ReleaseCom(infos);
            ReleaseCom(manager);
        }
        return result;
    }

    private ScanAcquisition Acquire(AppSettings settings)
    {
        dynamic? manager = null;
        dynamic? infos = null;
        dynamic? selectedInfo = null;
        dynamic? device = null;
        dynamic? items = null;
        dynamic? item = null;
        try
        {
            manager = CreateComObject("WIA.DeviceManager");
            infos = manager.DeviceInfos;
            selectedInfo = FindConfiguredDevice(infos, settings);
            if (selectedInfo is null) throw new ScannerUnavailableException("The configured scanner is unavailable.");
            device = selectedInfo.Connect();
            ApplyDeviceSettings(device, settings);
            items = device.Items;
            if ((int)items.Count < 1) throw new ScannerUnavailableException("The configured scanner has no acquisition source.");
            item = items[1];
            ApplyItemSettings(item, settings);
            return TransferPages(item, settings.MaximumPages, SelectTransferFormat(item, settings.Duplex));
        }
        catch (COMException exception) when (exception.HResult == WiaErrorDeviceCommunication)
        {
            throw new ScannerUnavailableException("The configured scanner cannot be contacted.");
        }
        finally
        {
            ReleaseCom(item);
            ReleaseCom(items);
            ReleaseCom(device);
            ReleaseCom(selectedInfo);
            ReleaseCom(infos);
            ReleaseCom(manager);
        }
    }

    private dynamic? FindConfiguredDevice(dynamic infos, AppSettings settings)
    {
        dynamic? nameMatch = null;
        var count = (int)infos.Count;
        for (var index = 1; index <= count; index++)
        {
            dynamic? info = null;
            try
            {
                info = infos[index];
                if ((int)info.Type != WiaConstants.ScannerDeviceType) continue;
                var id = ReadPropertyAsString(info.Properties, WiaConstants.DeviceId);
                var name = ReadPropertyAsString(info.Properties, WiaConstants.DeviceName);
                if (!string.IsNullOrWhiteSpace(settings.ScannerDeviceId) &&
                    string.Equals(id, settings.ScannerDeviceId, StringComparison.Ordinal))
                {
                    ReleaseCom(nameMatch);
                    return info;
                }
                if (nameMatch is null && !string.IsNullOrWhiteSpace(settings.ScannerName) &&
                    string.Equals(name, settings.ScannerName, StringComparison.Ordinal))
                {
                    nameMatch = info;
                    info = null;
                }
            }
            finally { ReleaseCom(info); }
        }
        return nameMatch;
    }

    private void ApplyDeviceSettings(dynamic device, AppSettings settings)
    {
        var handling = WiaConstants.Feeder | WiaConstants.AutoAdvance;
        if (settings.Duplex) handling |= WiaConstants.Duplex;
        TrySetProperty(device.Properties, WiaConstants.DocumentHandlingSelect, handling, "document handling");
        TrySetProperty(device.Properties, WiaConstants.Pages, settings.MaximumPages, "maximum pages");
    }

    private void ApplyItemSettings(dynamic item, AppSettings settings)
    {
        TrySetProperty(item.Properties, WiaConstants.HorizontalResolution, settings.Dpi, "horizontal resolution");
        TrySetProperty(item.Properties, WiaConstants.VerticalResolution, settings.Dpi, "vertical resolution");
        var intent = settings.ColourMode switch
        {
            ScanColourMode.Colour => WiaConstants.IntentColour,
            ScanColourMode.Greyscale => WiaConstants.IntentGreyscale,
            _ => WiaConstants.IntentText
        };
        TrySetProperty(item.Properties, WiaConstants.CurrentIntent, intent, "colour mode");

        var requestedPage = settings.PaperSize == ScanPaperSize.Automatic ? WiaConstants.PageAuto : WiaConstants.PageA4;
        if (!TrySetProperty(item.Properties, WiaConstants.PageSize, requestedPage, "paper size") &&
            settings.PaperSize == ScanPaperSize.Automatic)
        {
            TrySetProperty(item.Properties, WiaConstants.PageSize, WiaConstants.PageA4, "A4 fallback");
        }
        if (settings.PaperSize == ScanPaperSize.A4)
        {
            TrySetProperty(item.Properties, WiaConstants.HorizontalExtent, (int)Math.Round(8.27 * settings.Dpi), "A4 width");
            TrySetProperty(item.Properties, WiaConstants.VerticalExtent, (int)Math.Round(11.69 * settings.Dpi), "A4 height");
        }
    }

    private string SelectTransferFormat(dynamic item, bool duplex)
    {
        dynamic? formats = null;
        try
        {
            formats = item.Formats;
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var count = (int)formats.Count;
            for (var index = 1; index <= count; index++)
                supported.Add(Convert.ToString(formats[index]) ?? string.Empty);
            var preference = duplex
                ? new[] { WiaConstants.TiffFormat, WiaConstants.JpegFormat, WiaConstants.BmpFormat }
                : new[] { WiaConstants.JpegFormat, WiaConstants.TiffFormat, WiaConstants.BmpFormat };
            var selected = preference.FirstOrDefault(supported.Contains);
            if (selected is null) throw new InvalidDataException("The scanner does not advertise JPEG, BMP, or TIFF transfer support.");
            return selected;
        }
        finally { ReleaseCom(formats); }
    }

    private ScanAcquisition TransferPages(dynamic item, int maximumPages, string format)
    {
        var pages = new List<ScanPage>();
        for (var index = 0; index < maximumPages; index++)
        {
            dynamic? image = null;
            dynamic? fileData = null;
            try
            {
                image = item.Transfer(format);
                fileData = image.FileData;
                var bytes = (byte[])fileData.BinaryData;
                var returnedFormat = ((string?)image.FormatID ?? format).ToUpperInvariant();
                pages.Add(new(bytes, FormatName(returnedFormat)));
            }
            catch (COMException exception) when (exception.HResult == WiaErrorPaperEmpty)
            {
                break;
            }
            finally
            {
                ReleaseCom(fileData);
                ReleaseCom(image);
            }
        }
        if (pages.Count == 0) throw new NoPagesException("No document or pages were acquired.");
        return new(pages);
    }

    private bool TrySetProperty(dynamic properties, int propertyId, object value, string settingName)
    {
        dynamic? property = null;
        try
        {
            var count = (int)properties.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic? candidate = null;
                try
                {
                    candidate = properties[index];
                    if ((int)candidate.PropertyID != propertyId) continue;
                    property = candidate;
                    candidate = null;
                    break;
                }
                finally { ReleaseCom(candidate); }
            }
            if (property is null)
            {
                logger.LogInformation("WIA setting {SettingName} is unsupported", settingName);
                return false;
            }
            property.Value = value;
            return true;
        }
        catch (COMException exception)
        {
            logger.LogInformation(exception, "WIA driver rejected setting {SettingName}; a safe driver fallback will be used", settingName);
            return false;
        }
        finally
        {
            ReleaseCom(property);
            ReleaseCom(properties);
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

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId) ?? throw new PlatformNotSupportedException("Windows Image Acquisition is unavailable.");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Windows Image Acquisition could not be started.");
    }

    private static string FormatName(string formatId) => formatId switch
    {
        WiaConstants.JpegFormat => "jpeg",
        WiaConstants.BmpFormat => "bmp",
        WiaConstants.TiffFormat => "tiff",
        _ => throw new InvalidDataException("The scanner returned an unsupported image format.")
    };

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
