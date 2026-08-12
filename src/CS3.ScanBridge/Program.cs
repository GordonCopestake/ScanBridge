using System.Net;
using System.Security.Principal;
using Serilog;

namespace CS3.ScanBridge;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        using var instance = CreateInstanceMutex(out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("CS3 Scan Bridge is already running for this Windows user.", "CS3 Scan Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS3 Scan Bridge");
        var logDirectory = Path.Combine(localData, "Logs");
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(logDirectory, "scanbridge-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14, shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        WebApplication? app = null;
        try
        {
            SettingsStore store = new(localData);
            AppSettings settings;
            try { settings = await store.LoadAsync(); }
            catch (Exception exception)
            {
                Log.Error(exception, "Settings could not be loaded; safe defaults will be used");
                store = new SettingsStore(localData);
                settings = new AppSettings();
            }
            new TemporaryData().DeleteAbandonedDirectories(TimeSpan.FromDays(1));

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, settings.ListenerPort));
            builder.Services.AddSingleton<ISettingsStore>(store);
            builder.Services.AddSingleton<BridgeStatus>();
            builder.Services.AddSingleton<IScannerBackend, WiaScannerService>();
            builder.Services.AddSingleton<IScannerBackend, TwainScannerService>();
            builder.Services.AddSingleton<IScannerService, ScannerService>();
            builder.Services.AddSingleton<IPdfComposer, PdfComposer>();
            builder.Services.AddSingleton<ScanCoordinator>();
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
            app = builder.Build();
            BridgeWebApp.MapEndpoints(app);
            await app.StartAsync();

            ApplicationConfiguration.Initialize();
            using var tray = new TrayApplicationContext(app.Services, store, logDirectory, settings.ListenerPort);
            if (NeedsConfiguration(settings)) tray.ShowSettings(true);
            Application.Run(tray);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "CS3 Scan Bridge stopped unexpectedly");
            MessageBox.Show("CS3 Scan Bridge could not start. Open the log folder for details.", "CS3 Scan Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (app is not null)
            {
                try { await app.StopAsync(TimeSpan.FromSeconds(10)); }
                finally { await app.DisposeAsync(); }
            }
            await Log.CloseAndFlushAsync();
        }
    }

    private static Mutex CreateInstanceMutex(out bool ownsMutex)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        return new Mutex(true, $"Local\\CS3ScanBridge-{sid.Replace('\\', '-')}", out ownsMutex);
    }

    private static bool NeedsConfiguration(AppSettings settings) =>
        settings.AllowedOrigins.Count == 0 ||
        (string.IsNullOrWhiteSpace(settings.ScannerDeviceId) && string.IsNullOrWhiteSpace(settings.ScannerName));
}
