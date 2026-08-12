using System.Text.Json;

namespace CS3.ScanBridge;

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private AppSettings current = new();

    public SettingsStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CS3 Scan Bridge");
        SettingsPath = Path.Combine(directory, "settings.json");
    }

    public string SettingsPath { get; }
    public AppSettings Current => current.Copy();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                current = new();
                return Current;
            }

            await using var stream = File.OpenRead(SettingsPath);
            current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
            SettingsValidator.Validate(current);
            return Current;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SettingsValidator.Validate(settings);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporary, SettingsPath, true);
                current = settings.Copy();
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { gate.Release(); }
    }
}

public static class SettingsValidator
{
    private static readonly int[] AllowedDpi = [150, 200, 300, 600];

    public static void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!AllowedDpi.Contains(settings.Dpi)) throw new ArgumentException("DPI must be 150, 200, 300, or 600.");
        if (settings.JpegQuality is < 1 or > 100) throw new ArgumentException("JPEG quality must be from 1 to 100.");
        if (settings.ScanTimeoutSeconds is < 10 or > 600) throw new ArgumentException("Scan timeout must be from 10 to 600 seconds.");
        if (settings.MaximumPages is < 1 or > 100) throw new ArgumentException("Maximum pages must be from 1 to 100.");
        if (settings.ListenerPort is < 1024 or > 65535) throw new ArgumentException("Listener port must be from 1024 to 65535.");
        if (settings.AllowedOrigins.Any(origin => !OriginPolicy.IsValidConfiguredOrigin(origin)))
            throw new ArgumentException("Each allowed origin must be an exact HTTP or HTTPS origin without a path, query, fragment, user name, or wildcard.");
        if (settings.AllowedOrigins.Count != settings.AllowedOrigins.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Allowed origins must be unique.");
    }
}
