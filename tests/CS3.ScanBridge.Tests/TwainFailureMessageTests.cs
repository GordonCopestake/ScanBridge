using NTwain.Data;

namespace CS3.ScanBridge.Tests;

public sealed class TwainFailureMessageTests
{
    [Fact]
    public void OfflineStatusExplainsHowToReconnectScanner()
    {
        var message = TwainFailureMessage.Describe(
            "open the configured TWAIN scanner",
            ReturnCode.Failure,
            ConditionCode.CheckDeviceOnline);

        Assert.Contains("scanner is offline", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CheckDeviceOnline", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReturnCode.Busy, ConditionCode.Success)]
    [InlineData(ReturnCode.Failure, ConditionCode.MaxConnections)]
    [InlineData(ReturnCode.ScannerLocked, ConditionCode.Success)]
    public void LockedStatusExplainsHowToReleaseScanner(ReturnCode returnCode, ConditionCode conditionCode)
    {
        var message = TwainFailureMessage.Describe(
            "open the configured TWAIN scanner",
            returnCode,
            conditionCode);

        Assert.Contains("Another program is using the scanner", message, StringComparison.Ordinal);
        Assert.Contains("Close NAPS2", message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownStatusIncludesDiagnosticCodes()
    {
        var message = TwainFailureMessage.Describe(
            "enable the configured TWAIN scanner",
            ReturnCode.Failure,
            ConditionCode.OperationError);

        Assert.Contains("return code: Failure", message, StringComparison.Ordinal);
        Assert.Contains("condition code: OperationError", message, StringComparison.Ordinal);
    }
}
