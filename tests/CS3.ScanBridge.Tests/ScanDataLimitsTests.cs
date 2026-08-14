namespace CS3.ScanBridge.Tests;

public sealed class ScanDataLimitsTests
{
    [Fact]
    public void PageBufferRejectsDataAboveLimit()
    {
        var buffer = new ScanPageBuffer(3);
        buffer.Add([1, 2], "jpeg");

        Assert.Throws<ScanDataLimitException>(() => buffer.Add([3, 4], "jpeg"));
        Assert.Single(buffer.Pages);
    }

    [Fact]
    public void StreamRejectsWritesAboveLimit()
    {
        using var stream = new SizeLimitedMemoryStream(3);
        stream.Write([1, 2]);

        Assert.Throws<ScanDataLimitException>(() => stream.Write([3, 4]));
        Assert.Equal(2, stream.Length);
    }
}
