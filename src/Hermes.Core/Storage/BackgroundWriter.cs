namespace Hermes.Core.Storage;

/// <summary>
/// Runs writes off the caller's thread, one at a time, in the order they were queued. Because a clear queued after
/// an append runs after it, the append can never bring back what the clear removed.
/// </summary>
public sealed class BackgroundWriter
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>A queued write threw. Raised on a background thread.</summary>
    public event Action<Exception>? Failed;

    public void Enqueue(Action write)
    {
        lock (_gate)
            _tail = _tail.ContinueWith(_ => Run(write), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Completes when everything queued so far has been written (or has failed).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
            return _tail;
    }

    private void Run(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex);
        }
    }
}
