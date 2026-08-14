namespace CS3.ScanBridge.Tests;

public sealed class WiaDocumentSourceSelectorTests
{
    [Fact]
    public void LoadedFeederSelectsDuplexAdfWhenRequested()
    {
        var source = WiaDocumentSourceSelector.Select(1031, true);

        Assert.True(source.UsesFeeder);
        Assert.Equal("duplex ADF", source.Description);
    }

    [Fact]
    public void EmptyFeederSelectsFlatbedAndIgnoresDuplex()
    {
        var source = WiaDocumentSourceSelector.Select(1030, true);

        Assert.False(source.UsesFeeder);
        Assert.Equal("flatbed", source.Description);
    }

    [Fact]
    public void MissingStatusPrefersConfiguredAdfWorkflow()
    {
        var source = WiaDocumentSourceSelector.Select(null, false);

        Assert.True(source.UsesFeeder);
    }
}
