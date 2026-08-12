using System.Text.Json.Serialization;

namespace CS3.ScanBridge;

public enum ScanColourMode { Colour, Greyscale, BlackAndWhite }
public enum ScanPaperSize { Automatic, A4 }
public enum ScannerProvider { Wia, Twain }

public sealed record ScannerInfo(
    string Id,
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ScannerProvider Provider = ScannerProvider.Wia);

public sealed record ScanPage(byte[] Data, string Format);

public sealed record ScanAcquisition(IReadOnlyList<ScanPage> Pages);

public sealed record ScanRequest(string? CorrelationId, string? SuggestedFilename);

public sealed record ScanOutcome(byte[] Pdf, int PageCount, string Filename);

public sealed record PdfComposition(byte[] Data, int PageCount);

public sealed record BridgeError(string Message, string? ErrorId = null);

public sealed class BridgeStatus
{
    public bool Busy { get; set; }
    public DateTimeOffset? LastScanTime { get; set; }
    public string? LastScanResult { get; set; }
    public string? LastErrorId { get; set; }
}

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScannerProvider ScannerProvider { get; set; } = ScannerProvider.Wia;
    public string? ScannerDeviceId { get; set; }
    public string? ScannerName { get; set; }
    public int Dpi { get; set; } = 300;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanColourMode ColourMode { get; set; } = ScanColourMode.Greyscale;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanPaperSize PaperSize { get; set; } = ScanPaperSize.Automatic;
    public bool Duplex { get; set; } = true;
    public int JpegQuality { get; set; } = 85;
    public int ScanTimeoutSeconds { get; set; } = 90;
    public int MaximumPages { get; set; } = 10;
    public int ListenerPort { get; set; } = 9175;
    public List<string> AllowedOrigins { get; set; } = [];
    public bool StartWithWindows { get; set; }

    public AppSettings Copy() => new()
    {
        ScannerProvider = ScannerProvider,
        ScannerDeviceId = ScannerDeviceId,
        ScannerName = ScannerName,
        Dpi = Dpi,
        ColourMode = ColourMode,
        PaperSize = PaperSize,
        Duplex = Duplex,
        JpegQuality = JpegQuality,
        ScanTimeoutSeconds = ScanTimeoutSeconds,
        MaximumPages = MaximumPages,
        ListenerPort = ListenerPort,
        AllowedOrigins = [.. AllowedOrigins],
        StartWithWindows = StartWithWindows
    };
}

public sealed class ScannerUnavailableException(string message) : Exception(message);
public sealed class NoPagesException(string message) : Exception(message);
public sealed class ScanTimedOutException(string message, Exception? inner = null) : Exception(message, inner);
