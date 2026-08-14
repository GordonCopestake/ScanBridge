using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;

namespace CS3.ScanBridge.Tests;

internal sealed class MemorySettingsStore(AppSettings settings) : ISettingsStore
{
    private AppSettings current = settings;
    public string SettingsPath => "memory";
    public AppSettings Current => current.Copy();
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
    public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) { current = value.Copy(); return Task.CompletedTask; }
}

internal sealed class FakeScanner : IScannerService
{
    public IReadOnlyList<ScannerInfo> Scanners { get; set; } = [new("scanner-1", "Brother DSmobile DS-740D")];
    public Func<AppSettings, CancellationToken, Task<ScanAcquisition>> OnScan { get; set; } =
        (_, _) => Task.FromResult(new ScanAcquisition([ImageFactory.Page()]));
    public int ScanCallCount { get; private set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken) => Task.FromResult(Scanners);
    public Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ScanCallCount++;
        Started.TrySetResult();
        return OnScan(settings, cancellationToken);
    }
}

internal sealed class FakeBackend(ScannerProvider provider, IReadOnlyList<ScannerInfo> scanners) : IScannerBackend
{
    public ScannerProvider Provider { get; } = provider;
    public IReadOnlyList<ScannerInfo> Scanners { get; } = scanners;
    public int ScanCount { get; private set; }
    public int EnumerationCount { get; private set; }

    public Task<IReadOnlyList<ScannerInfo>> GetScannersAsync(CancellationToken cancellationToken)
    {
        EnumerationCount++;
        return Task.FromResult(Scanners);
    }

    public Task<ScanAcquisition> ScanAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ScanCount++;
        return Task.FromResult(new ScanAcquisition([ImageFactory.Page()]));
    }
}

internal sealed class BridgeTestHost : IAsyncDisposable
{
    private readonly WebApplication app;
    public HttpClient Client { get; }

    private BridgeTestHost(WebApplication app)
    {
        this.app = app;
        Client = app.GetTestClient();
    }

    public static async Task<BridgeTestHost> CreateAsync(FakeScanner scanner, AppSettings? settings = null, IPdfComposer? composer = null)
    {
        settings ??= new AppSettings
        {
            ScannerDeviceId = "scanner-1",
            ScannerName = "Brother DSmobile DS-740D",
            AllowedOrigins = ["https://cs3.example.test"]
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISettingsStore>(new MemorySettingsStore(settings));
        builder.Services.AddSingleton<IScannerService>(scanner);
        builder.Services.AddSingleton<IPdfComposer>(composer ?? new PdfComposer());
        builder.Services.AddSingleton<BridgeStatus>();
        builder.Services.AddSingleton<ScanCoordinator>();
        var app = builder.Build();
        BridgeWebApp.MapEndpoints(app);
        await app.StartAsync();
        return new BridgeTestHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

internal static class ImageFactory
{
    public static ScanPage Page(System.Drawing.Color? colour = null)
    {
        using var bitmap = new System.Drawing.Bitmap(40, 60);
        bitmap.SetResolution(300, 300);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(colour ?? System.Drawing.Color.White);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
        return new ScanPage(stream.ToArray(), "jpeg");
    }
}

internal static class RequestFactory
{
    public static HttpRequestMessage Scan(string origin = "https://cs3.example.test", bool customHeader = true,
        string json = "{}")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/scan")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        if (customHeader) request.Headers.TryAddWithoutValidation("X-CS3-Scan-Request", "1");
        return request;
    }
}
