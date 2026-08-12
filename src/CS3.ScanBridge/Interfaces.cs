namespace CS3.ScanBridge;

public interface IScannerService
{
    Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken);
    Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IScannerBackend
{
    ScannerProvider Provider { get; }
    Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken);
    Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IPdfComposer
{
    PdfComposition Compose(IReadOnlyList<ScanPage> pages, int jpegQuality, int maximumPages);
}

public interface ISettingsStore
{
    string SettingsPath { get; }
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
