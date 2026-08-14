using NAPS2.Images;
using NAPS2.Scan;

namespace CS3.ScanBridge.Tests;

public sealed class ScannerConfigurationTests
{
    [Fact]
    public void DeviceSelectionFallsBackFromStaleIdToName()
    {
        var expected = new ScanDevice(Driver.Twain, "new-id", "Brother DS-740D");
        var settings = new AppSettings
        {
            ScannerDeviceId = "old-id",
            ScannerName = "Brother DS-740D"
        };

        var result = ScannerConfiguration.SelectDevice(settings, [expected]);

        Assert.Same(expected, result);
    }

    [Fact]
    public void TwainOptionsMatchBrotherProfile()
    {
        var device = new ScanDevice(Driver.Twain, "id", "Brother");
        var settings = new AppSettings
        {
            Dpi = 600,
            ColourMode = ScanColourMode.Colour,
            Duplex = true,
            JpegQuality = 72
        };

        var options = ScannerConfiguration.CreateTwainOptions(settings, device);

        Assert.Equal(600, options.Dpi);
        Assert.Equal(BitDepth.Color, options.BitDepth);
        Assert.Equal(PaperSource.Duplex, options.PaperSource);
        Assert.Equal(PageSize.A4, options.PageSize);
        Assert.Equal(72, options.Quality);
        Assert.NotNull(options.TwainOptions);
    }

    [Fact]
    public void WiaOptionsUseFlatbedWhenFeederIsEmpty()
    {
        var device = new ScanDevice(Driver.Wia, "id", "Kyocera");
        var settings = new AppSettings { Duplex = true, PaperSize = ScanPaperSize.A4 };

        var options = ScannerConfiguration.CreateWiaOptions(
            settings, device, new WiaDocumentSourceSelection(false, "flatbed"));

        Assert.Equal(PaperSource.Flatbed, options.PaperSource);
        Assert.Equal(PageSize.A4, options.PageSize);
        Assert.NotNull(options.WiaOptions);
    }
}
