namespace CS3.ScanBridge.Tests;

public sealed class ScannerFailureMessageTests
{
    [Fact]
    public void KnownNestedDriverFaultIsMapped()
    {
        var exception = new InvalidOperationException("wrapper", new DeviceBusyException("busy"));

        var result = ScannerFailureMessage.DescribeKnown("TWAIN", exception);

        Assert.Equal("Another program is using the TWAIN scanner.", result);
    }

    [Fact]
    public void UnexpectedFaultIsNotMasked()
    {
        Assert.Null(ScannerFailureMessage.DescribeKnown("WIA", new InvalidOperationException("bug")));
    }

    private sealed class DeviceBusyException(string message) : Exception(message);
}
