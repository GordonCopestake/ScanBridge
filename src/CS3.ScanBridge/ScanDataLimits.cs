namespace CS3.ScanBridge;

internal static class ScanLimits
{
    public const long MaximumAcquiredBytes = 128L * 1024 * 1024;
    public const long MaximumPdfBytes = 160L * 1024 * 1024;
}

internal sealed class ScanPageBuffer
{
    private readonly long maximumBytes;
    private readonly List<ScanPage> pages = [];
    private long totalBytes;

    public ScanPageBuffer(long maximumBytes = ScanLimits.MaximumAcquiredBytes) => this.maximumBytes = maximumBytes;
    public int Count => pages.Count;
    public IReadOnlyList<ScanPage> Pages => pages;
    public long RemainingBytes => maximumBytes - totalBytes;

    public void Add(byte[] data, string format)
    {
        if (data.LongLength > RemainingBytes)
            throw new ScanDataLimitException("The acquired scan data exceeded the safe memory limit.");
        pages.Add(new ScanPage(data, format));
        totalBytes += data.LongLength;
    }
}

internal sealed class SizeLimitedMemoryStream(long maximumLength) : MemoryStream
{
    private void CheckWrite(int count)
    {
        if (count < 0 || Position > maximumLength - count)
            throw new ScanDataLimitException("Generated scan data exceeded the safe memory limit.");
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckWrite(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckWrite(buffer.Length);
        base.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckWrite(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        CheckWrite(1);
        base.WriteByte(value);
    }
}
