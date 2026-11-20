namespace Hermes.Core.Storage;

/// <summary>
/// Runs writes off the caller's thread, one at a time, in the order they were queued. Because a clear queued after
/// an append runs after it, the append can never bring back what the clear removed.
/// </summary>
public sealed class BackgroundWriter
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>A queued write threw. Raised on a background thread.</summary>
    public event Action<Exception>? Failed;

    public void Enqueue(Action write)
    {
        lock (_gate)
            _tail = _tail.ContinueWith(_ => Run(write), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Queues <paramref name="write"/> like <see cref="Enqueue"/>, for a caller that waits for its result: the task
    /// completes once it has run, and fails with its exception instead of raising <see cref="Failed"/>. The writes
    /// after it still run.
    /// </summary>
    public Task EnqueueAsync(Action write)
    {
        lock (_gate)
        {
            var written = _tail.ContinueWith(_ => write(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            // The queue goes on after a failed write; its failure belongs to the caller awaiting it.
            _tail = written.ContinueWith(static _ => { }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return written;
        }
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
