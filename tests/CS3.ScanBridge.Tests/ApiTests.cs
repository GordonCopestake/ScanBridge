using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CS3.ScanBridge.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task MissingCustomHeaderReturns400WithoutScanning()
    {
        var scanner = new FakeScanner();
        await using var host = await BridgeTestHost.CreateAsync(scanner);
        using var response = await host.Client.SendAsync(RequestFactory.Scan(customHeader: false));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, scanner.ScanCallCount);
    }

    [Fact]
    public async Task DisallowedOriginReturns403WithoutScanning()
    {
        var scanner = new FakeScanner();
        await using var host = await BridgeTestHost.CreateAsync(scanner);
        using var response = await host.Client.SendAsync(RequestFactory.Scan("https://cs3.example.test.evil.invalid"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, scanner.ScanCallCount);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PreflightReturnsRequiredCorsHeaders()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeScanner());
        using var request = new HttpRequestMessage(HttpMethod.Options, "/scan");
        request.Headers.TryAddWithoutValidation("Origin", "https://cs3.example.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type,x-cs3-scan-request");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("https://cs3.example.test", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("X-CS3-Scan-Request", response.Headers.GetValues("Access-Control-Allow-Headers").Single());
    }

    [Fact]
    public async Task AllowedPrivateNetworkPreflightReturnsHeader()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeScanner());
        using var request = new HttpRequestMessage(HttpMethod.Options, "/scan");
        request.Headers.TryAddWithoutValidation("Origin", "https://cs3.example.test");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Private-Network", "true");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
    }

    [Fact]
    public async Task HealthReportsReadyAndScanner()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeScanner());
        using var response = await host.Client.GetAsync("/health");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ready", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("scannerAvailable").GetBoolean());
        Assert.Equal("Wia", json.GetProperty("scannerProvider").GetString());
    }

    [Fact]
    public async Task ScannersResponseIncludesProvider()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeScanner());
        using var response = await host.Client.GetAsync("/scanners", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Wia", json[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task BusyScanReturns409Immediately()
    {
        var completion = new TaskCompletionSource<ScanAcquisition>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner { OnScan = (_, _) => completion.Task };
        await using var host = await BridgeTestHost.CreateAsync(scanner);
        var first = host.Client.SendAsync(RequestFactory.Scan());
        await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var second = await host.Client.SendAsync(RequestFactory.Scan());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        completion.SetResult(new([ImageFactory.Page()]));
        using var firstResponse = await first;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    [Fact]
    public async Task UnavailableScannerReturns503()
    {
        var scanner = new FakeScanner { Scanners = [] };
        await using var host = await BridgeTestHost.CreateAsync(scanner);
        using var response = await host.Client.SendAsync(RequestFactory.Scan());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, scanner.ScanCallCount);
    }

    [Fact]
    public async Task ScanTimeoutReturns504()
    {
        var completion = new TaskCompletionSource<ScanAcquisition>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new FakeScanner { OnScan = (_, _) => completion.Task };
        var settings = new AppSettings { ScannerDeviceId = "scanner-1", AllowedOrigins = ["https://cs3.example.test"], ScanTimeoutSeconds = 0 };
        await using var host = await BridgeTestHost.CreateAsync(scanner, settings);
        using var response = await host.Client.SendAsync(RequestFactory.Scan());
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        completion.SetResult(new([ImageFactory.Page()]));
    }

    [Fact]
    public async Task PdfResponseHasSafeHeaders()
    {
        await using var host = await BridgeTestHost.CreateAsync(new FakeScanner());
        using var response = await host.Client.SendAsync(RequestFactory.Scan(json: "{\"suggestedFilename\":\"note.pdf\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(response.Content.Headers.ContentLength);
        Assert.Equal("note.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task UnexpectedErrorsDoNotLeakDetails()
    {
        var scanner = new FakeScanner { OnScan = (_, _) => throw new InvalidOperationException("secret driver details") };
        await using var host = await BridgeTestHost.CreateAsync(scanner);
        using var response = await host.Client.SendAsync(RequestFactory.Scan());
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret driver details", content);
        Assert.Contains("errorId", content);
    }
}
