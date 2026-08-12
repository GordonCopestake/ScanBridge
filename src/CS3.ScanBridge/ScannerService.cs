namespace CS3.ScanBridge;

public sealed class ScannerService(IEnumerable<IScannerBackend> backends, ILogger<ScannerService> logger) : IScannerService
{
    private readonly IReadOnlyDictionary<ScannerProvider, IScannerBackend> backends =
        backends.ToDictionary(value => value.Provider);

    public async Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken)
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
        return scanners;
    }

    public Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!backends.TryGetValue(settings.ScannerProvider, out var backend))
            throw new ScannerUnavailableException($"The configured {settings.ScannerProvider} scanner provider is unavailable.");
        return backend.ScanAsync(settings, cancellationToken);
    }
}
