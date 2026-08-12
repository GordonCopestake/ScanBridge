namespace CS3.ScanBridge.Tests;

public sealed class ScannerServiceTests
{
    [Fact]
    public async Task EnumerationCombinesWiaAndTwainWithoutChangingIdentity()
    {
        var wia = new FakeBackend(ScannerProvider.Wia, [new("wia-id", "Printer", ScannerProvider.Wia)]);
        var twain = new FakeBackend(ScannerProvider.Twain, [new("TW-Brother DS-740D", "TW-Brother DS-740D", ScannerProvider.Twain)]);
        var service = new ScannerService([wia, twain], NullLogger<ScannerService>.Instance);

        var scanners = await service.GetScannersAsync(TestContext.Current.CancellationToken);

        Assert.Contains(scanners, value => value.Provider == ScannerProvider.Wia && value.Id == "wia-id");
        Assert.Contains(scanners, value => value.Provider == ScannerProvider.Twain && value.Id == "TW-Brother DS-740D");
    }

    [Fact]
    public async Task ScanUsesOnlyTheConfiguredProvider()
    {
        var wia = new FakeBackend(ScannerProvider.Wia, []);
        var twain = new FakeBackend(ScannerProvider.Twain, []);
        var service = new ScannerService([wia, twain], NullLogger<ScannerService>.Instance);
        var settings = new AppSettings { ScannerProvider = ScannerProvider.Twain, ScannerDeviceId = "TW-Brother DS-740D" };

        await service.ScanAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(0, wia.ScanCount);
        Assert.Equal(1, twain.ScanCount);
    }
}
