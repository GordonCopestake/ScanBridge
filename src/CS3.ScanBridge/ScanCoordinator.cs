using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CS3.ScanBridge;

public sealed class ScanCoordinator
{
    private readonly IScannerService scanner;
    private readonly IPdfComposer pdfComposer;
    private readonly ISettingsStore settingsStore;
    private readonly BridgeStatus status;
    private readonly ILogger<ScanCoordinator> logger;
    private int busy;

    public ScanCoordinator(IScannerService scanner, IPdfComposer pdfComposer, ISettingsStore settingsStore,
        BridgeStatus status, ILogger<ScanCoordinator> logger)
    {
        this.scanner = scanner;
        this.pdfComposer = pdfComposer;
        this.settingsStore = settingsStore;
        this.status = status;
        this.logger = logger;
    }

    public bool IsBusy => Volatile.Read(ref busy) != 0;

    public async Task<ScanOutcome> ScanAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new ScannerBusyException();
        status.Busy = true;
        var settings = settingsStore.Current;
        var stopwatch = Stopwatch.StartNew();
        var releaseHere = true;
        CancellationTokenSource? scanCancellation = null;
        try
        {
            logger.LogInformation("Scan started for {Provider} device {DeviceName}; DPI {Dpi}; colour {ColourMode}; duplex {Duplex}; maximum pages {MaximumPages}; correlation {CorrelationId}",
                settings.ScannerProvider, settings.ScannerName, settings.Dpi, settings.ColourMode, settings.Duplex, settings.MaximumPages, request.CorrelationId);
            scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            scanCancellation.CancelAfter(TimeSpan.FromSeconds(settings.ScanTimeoutSeconds));
            var acquisitionTask = scanner.ScanAsync(settings, scanCancellation.Token);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(settings.ScanTimeoutSeconds), CancellationToken.None);
            if (await Task.WhenAny(acquisitionTask, timeoutTask) != acquisitionTask)
            {
                await scanCancellation.CancelAsync();
                releaseHere = false;
                _ = ObserveLateCompletionAndReleaseAsync(acquisitionTask, scanCancellation);
                scanCancellation = null;
                throw new ScanTimedOutException("The scan did not finish before the configured timeout.");
            }

            var acquisition = await acquisitionTask;
            if (acquisition.Pages.Count == 0) throw new NoPagesException("No document or pages were acquired.");
            var pdf = pdfComposer.Compose(acquisition.Pages, settings.JpegQuality, settings.MaximumPages);
            var filename = FilenameSanitizer.Sanitize(request.SuggestedFilename, DateTimeOffset.Now);
            status.LastScanTime = DateTimeOffset.Now;
            status.LastScanResult = $"Success: {pdf.PageCount} page(s)";
            status.LastErrorId = null;
            logger.LogInformation("Scan completed in {ElapsedMilliseconds} ms with {PageCount} page source(s); result success",
                stopwatch.ElapsedMilliseconds, pdf.PageCount);
            return new(pdf.Data, pdf.PageCount, filename);
        }
        catch (Exception exception) when (exception is not ScannerBusyException)
        {
            status.LastScanTime = DateTimeOffset.Now;
            status.LastScanResult = exception is ScanTimedOutException ? "Timed out" : "Failed";
            logger.LogWarning(exception, "Scan ended in {ElapsedMilliseconds} ms; result {Result}", stopwatch.ElapsedMilliseconds, status.LastScanResult);
            throw;
        }
        finally
        {
            scanCancellation?.Dispose();
            if (releaseHere) ReleaseBusy();
        }
    }

    private async Task ObserveLateCompletionAndReleaseAsync(Task task, CancellationTokenSource scanCancellation)
    {
        try { await task; }
        catch (Exception exception) { logger.LogWarning(exception, "Scanner operation ended after the HTTP scan timeout"); }
        finally
        {
            scanCancellation.Dispose();
            ReleaseBusy();
        }
    }

    private void ReleaseBusy()
    {
        status.Busy = false;
        Interlocked.Exchange(ref busy, 0);
    }
}

public sealed class ScannerBusyException : Exception;
