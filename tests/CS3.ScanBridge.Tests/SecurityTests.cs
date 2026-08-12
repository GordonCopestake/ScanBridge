namespace CS3.ScanBridge.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void ExactOriginMatchingIsOrdinal()
    {
        Assert.True(OriginPolicy.IsAllowed("https://cs3.example.test", ["https://cs3.example.test"]));
        Assert.True(OriginPolicy.IsAllowed("https://cs3.example.test", ["https://cs3.example.test/"]));
        Assert.False(OriginPolicy.IsAllowed("https://CS3.example.test", ["https://cs3.example.test"]));
    }

    [Theory]
    [InlineData("https://*.example.test")]
    [InlineData("https://example.test/cs3")]
    public void WildcardAndPathOriginsAreInvalidConfiguration(string origin)
    {
        Assert.False(OriginPolicy.IsValidConfiguredOrigin(origin));
    }

    [Fact]
    public void PartialHostnameDoesNotMatchAllowedOrigin()
    {
        Assert.False(OriginPolicy.IsAllowed("https://cs3.example.test.evil.invalid", ["https://cs3.example.test"]));
    }

    [Fact]
    public void FilenameIsSanitized()
    {
        var name = FilenameSanitizer.Sanitize("..\\unsafe:name", new DateTimeOffset(2026, 8, 11, 12, 30, 0, TimeSpan.Zero));
        Assert.Equal("unsafename.pdf", name);
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain('\\', name);
    }
}
