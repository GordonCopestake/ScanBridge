namespace CS3.ScanBridge;

public sealed class ScannerService(IEnumerable<IScannerBackend> backends, ILogger<ScannerService> logger) : IScannerService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private readonly IReadOnlyDictionary<ScannerProvider, IScannerBackend> backends =
        backends.ToDictionary(value => value.Provider);
    private readonly SemaphoreSlim discoveryLock = new(1, 1);
    private IReadOnlyList<ScannerInfo>? cachedScanners;
    private long cacheExpiresAtUtcTicks;

    public async Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken)
    {
        var cached = Volatile.Read(ref cachedScanners);
        if (cached is not null && DateTime.UtcNow.Ticks < Volatile.Read(ref cacheExpiresAtUtcTicks)) return cached;

        await discoveryLock.WaitAsync(cancellationToken);
        try
        {
            cached = Volatile.Read(ref cachedScanners);
            if (cached is not null && DateTime.UtcNow.Ticks < Volatile.Read(ref cacheExpiresAtUtcTicks)) return cached;

            cached = await DiscoverScannersAsync(cancellationToken);
            Volatile.Write(ref cachedScanners, cached);
            Volatile.Write(ref cacheExpiresAtUtcTicks, DateTime.UtcNow.Add(CacheDuration).Ticks);
            return cached;
        }
        finally { discoveryLock.Release(); }
    }

    private async Task<IReadOnlyList<ScannerInfo>> DiscoverScannersAsync(CancellationToken cancellationToken)
    {
        var scanners = new List<ScannerInfo>();
        foreach (var backend in backends.Values.OrderBy(value => value.Provider))
        {
            try { scanners.AddRange(await backend.GetScannersAsync(cancellationToken)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Provider} scanner enumeration failed", backend.Provider);
                // One unavailable provider must not hide devices from the other provider.
            }
        }
        return scanners.ToArray();
    }

    public Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!backends.TryGetValue(settings.ScannerProvider, out var backend))
            throw new ScannerUnavailableException($"The configured {settings.ScannerProvider} scanner provider is unavailable.");
        return backend.ScanAsync(settings, cancellationToken);
    }
}
