namespace CS3.ScanBridge;

internal readonly record struct WiaDocumentSourceSelection(bool UsesFeeder, string Description);

internal static class WiaDocumentSourceSelector
{
    public static WiaDocumentSourceSelection Select(int? documentHandlingStatus, bool duplex)
    {
        var feederReady = documentHandlingStatus is null ||
                          (documentHandlingStatus.Value & WiaConstants.FeederReady) != 0;
        if (!feederReady) return new(false, "flatbed");
        return new(true, duplex ? "duplex ADF" : "ADF");
    }
}
