using System.Net.Mime;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Net.Http.Headers;

namespace CS3.ScanBridge;

public static class BridgeWebApp
{
    private const int MaximumRequestBytes = 4096;
    private static readonly HashSet<string> AllowedScanProperties = new(StringComparer.Ordinal)
    {
        "correlationId", "suggestedFilename"
    };

    public static void MapEndpoints(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception exception)
            {
                if (context.Response.HasStarted) throw;
                var errorId = Guid.NewGuid().ToString("N");
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HttpErrors")
                    .LogError(exception, "Unexpected HTTP error {ErrorId}", errorId);
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new BridgeError("The request could not be completed.", errorId));
            }
        });
        app.Use(async (context, next) =>
        {
            var settings = context.RequestServices.GetRequiredService<ISettingsStore>().Current;
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin) && !OriginPolicy.IsAllowed(origin, settings.AllowedOrigins))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new BridgeError("The origin is not allowed."));
                return;
            }
            if (OriginPolicy.IsAllowed(origin, settings.AllowedOrigins))
                context.Response.Headers.AccessControlAllowOrigin = origin;
            context.Response.Headers.Append(HeaderNames.Vary, "Origin");
            await next();
        });

        app.MapGet("/health", GetHealthAsync);
        app.MapGet("/scanners", async (IScannerService scanner, CancellationToken cancellationToken) =>
            Results.Ok(await scanner.GetScannersAsync(cancellationToken)));
        app.MapMethods("/scan", ["OPTIONS"], Preflight);
        app.MapPost("/scan", ScanAsync);
    }

    private static async Task<IResult> GetHealthAsync(ISettingsStore store, IScannerService scanner,
        ScanCoordinator coordinator, CancellationToken cancellationToken)
    {
        var settings = store.Current;
        IReadOnlyList<ScannerInfo> scanners;
        try { scanners = await scanner.GetScannersAsync(cancellationToken); }
        catch { scanners = []; }
        var available = FindConfiguredScanner(settings, scanners);
        return Results.Json(new
        {
            service = "CS3 Scan Bridge",
            status = "ready",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            busy = coordinator.IsBusy,
            scannerConfigured = !string.IsNullOrWhiteSpace(settings.ScannerDeviceId) || !string.IsNullOrWhiteSpace(settings.ScannerName),
            scannerAvailable = available is not null,
            scannerName = available?.Name ?? settings.ScannerName,
            scannerProvider = (available?.Provider ?? settings.ScannerProvider).ToString()
        });
    }

    private static IResult Preflight(HttpContext context, ISettingsStore store)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!OriginPolicy.IsAllowed(origin, store.Current.AllowedOrigins))
            return JsonError(StatusCodes.Status403Forbidden, "The origin is missing or is not allowed.");

        context.Response.Headers.AccessControlAllowMethods = "POST";
        context.Response.Headers.AccessControlAllowHeaders = "Content-Type, X-CS3-Scan-Request";
        context.Response.Headers.AccessControlMaxAge = "600";
        if (string.Equals(context.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        return Results.NoContent();
    }

    private static async Task<IResult> ScanAsync(HttpContext context, ISettingsStore store, IScannerService scanner,
        ScanCoordinator coordinator, BridgeStatus status, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ScanEndpoint");
        var origin = context.Request.Headers.Origin.ToString();
        if (!OriginPolicy.IsAllowed(origin, store.Current.AllowedOrigins))
            return JsonError(StatusCodes.Status403Forbidden, "The origin is missing or is not allowed.");
        if (!context.Request.HasJsonContentType())
            return JsonError(StatusCodes.Status400BadRequest, "Content-Type must be application/json.");
        if (!string.Equals(context.Request.Headers["X-CS3-Scan-Request"], "1", StringComparison.Ordinal))
            return JsonError(StatusCodes.Status400BadRequest, "The required scan request header is missing or invalid.");

        ScanRequest request;
        try
        {
            if (context.Request.ContentLength > MaximumRequestBytes)
                return JsonError(StatusCodes.Status413PayloadTooLarge, "The scan request is too large.");
            using var body = await ReadRequestBodyAsync(context.Request.Body, context.RequestAborted);
            if (body is null)
                return JsonError(StatusCodes.Status413PayloadTooLarge, "The scan request is too large.");
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Any(property => !AllowedScanProperties.Contains(property.Name)))
                return JsonError(StatusCodes.Status400BadRequest, "The scan request contains unsupported fields.");
            request = document.RootElement.Deserialize<ScanRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new(null, null);
            if (request.CorrelationId?.Length > 100 || request.SuggestedFilename?.Length > 255)
                return JsonError(StatusCodes.Status400BadRequest, "A scan request field is too long.");
        }
        catch (JsonException) { return JsonError(StatusCodes.Status400BadRequest, "The request JSON is invalid."); }

        if (coordinator.IsBusy) return JsonError(StatusCodes.Status409Conflict, "The scanner is busy.");
        try
        {
            var outcome = await coordinator.ScanAsync(request, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentLength = outcome.Pdf.LongLength;
            return Results.File(outcome.Pdf, MediaTypeNames.Application.Pdf, outcome.Filename, enableRangeProcessing: false);
        }
        catch (ScannerBusyException) { return JsonError(StatusCodes.Status409Conflict, "The scanner is busy."); }
        catch (NoPagesException) { return JsonError(StatusCodes.Status422UnprocessableEntity, "No document or pages were acquired."); }
        catch (ScannerUnavailableException) { return JsonError(StatusCodes.Status503ServiceUnavailable, "The configured scanner is unavailable."); }
        catch (ScanTimedOutException) { return JsonError(StatusCodes.Status504GatewayTimeout, "The scan timed out."); }
        catch (ScanDataLimitException) { return JsonError(StatusCodes.Status413PayloadTooLarge, "The scanned document is too large."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return JsonError(499, "The request was cancelled.");
        }
        catch (Exception exception)
        {
            var errorId = Guid.NewGuid().ToString("N");
            status.LastErrorId = errorId;
            logger.LogError(exception, "Unexpected scan or PDF error {ErrorId}", errorId);
            return JsonError(StatusCodes.Status500InternalServerError, "The scan could not be completed.", errorId);
        }
    }

    private static async Task<MemoryStream?> ReadRequestBodyAsync(Stream input, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                output.Position = 0;
                return output;
            }
            if (output.Length + read > MaximumRequestBytes)
            {
                output.Dispose();
                return null;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static ScannerInfo? FindConfiguredScanner(AppSettings settings, IReadOnlyList<ScannerInfo> scanners) =>
        scanners.FirstOrDefault(value => value.Provider == settings.ScannerProvider &&
            !string.IsNullOrWhiteSpace(settings.ScannerDeviceId) &&
            string.Equals(value.Id, settings.ScannerDeviceId, StringComparison.Ordinal)) ??
        scanners.FirstOrDefault(value => value.Provider == settings.ScannerProvider &&
            !string.IsNullOrWhiteSpace(settings.ScannerName) &&
            string.Equals(value.Name, settings.ScannerName, StringComparison.Ordinal));

    private static IResult JsonError(int status, string message, string? errorId = null) =>
        Results.Json(new BridgeError(message, errorId), statusCode: status);
}
