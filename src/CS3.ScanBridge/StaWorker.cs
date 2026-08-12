using System.Collections.Concurrent;

namespace CS3.ScanBridge;

public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = [];
    private readonly Thread thread;
    private bool disposed;

    public StaWorker()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "CS3 Scan Bridge WIA STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }
            try { completion.TrySetResult(action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }, cancellationToken);
        return completion.Task;
    }

    private void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable()) action();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        queue.CompleteAdding();
        if (Thread.CurrentThread != thread && thread.Join(TimeSpan.FromSeconds(5))) queue.Dispose();
    }
}
