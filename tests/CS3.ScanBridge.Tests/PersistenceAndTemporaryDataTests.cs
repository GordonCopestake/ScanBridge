namespace CS3.ScanBridge.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SettingsValidateAndPersistAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CS3ScanBridgeTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var settings = new AppSettings
            {
                ScannerDeviceId = "id",
                ScannerName = "DS-740D",
                ScannerProvider = ScannerProvider.Twain,
                Dpi = 600,
                AllowedOrigins = ["https://cs3.example.test"]
            };
            await store.SaveAsync(settings);
            var reloaded = await new SettingsStore(directory).LoadAsync();
            Assert.Equal(600, reloaded.Dpi);
            Assert.Equal(ScannerProvider.Twain, reloaded.ScannerProvider);
            Assert.Equal("https://cs3.example.test", reloaded.AllowedOrigins.Single());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            settings.Dpi = 301;
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
