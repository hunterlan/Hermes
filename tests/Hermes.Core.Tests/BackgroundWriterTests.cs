using System.Collections.Concurrent;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

/// <summary>Writes a caller waits for: off the caller's thread, in order, each failure only its own caller's.</summary>
public class BackgroundWriterTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_write_runs_off_the_calling_thread_and_its_task_completes_after_it()
    {
        var writer = new BackgroundWriter();
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        // Taken now: after the await below this test may go on on the write's own pool thread.
        var callingThread = Environment.CurrentManagedThreadId;
        var writeThread = 0;

        var writing = writer.EnqueueAsync(() =>
        {
            writeThread = Environment.CurrentManagedThreadId;
            started.Set();
            Assert.True(gate.Wait(GateTimeout));
        });

        // This thread is blocked here, so the write that started meanwhile runs on another one.
        Assert.True(started.Wait(GateTimeout, TestContext.Current.CancellationToken));
        Assert.False(writing.IsCompleted);
        gate.Set();
        await writing;
        Assert.NotEqual(0, writeThread);
        Assert.NotEqual(callingThread, writeThread);
    }

    [Fact]
    public async Task Writes_run_one_at_a_time_in_the_order_they_were_queued()
    {
        var writer = new BackgroundWriter();
        using var gate = new ManualResetEventSlim();
        var order = new ConcurrentQueue<string>();

        var first = writer.EnqueueAsync(() =>
        {
            Assert.True(gate.Wait(GateTimeout));
            order.Enqueue("save");
        });
        var second = writer.EnqueueAsync(() => order.Enqueue("delete"));
        writer.Enqueue(() => order.Enqueue("history"));
        gate.Set();

        await Task.WhenAll(first, second, writer.FlushAsync());
        Assert.Equal(["save", "delete", "history"], order);
    }

    [Fact]
    public async Task A_failed_write_fails_only_its_own_task_and_the_queue_goes_on()
    {
        var writer = new BackgroundWriter();
        var raised = new ConcurrentQueue<Exception>();
        writer.Failed += raised.Enqueue;
        var later = false;

        var failing = writer.EnqueueAsync(() => throw new IOException("disk full"));
        var after = writer.EnqueueAsync(() => later = true);

        var thrown = await Assert.ThrowsAsync<IOException>(() => failing);
        Assert.Equal("disk full", thrown.Message);
        await after;
        Assert.True(later);
        // The caller has the failure; it isn't raised a second time.
        Assert.Empty(raised);
    }

    [Fact]
    public async Task Flushing_waits_for_queued_writes_and_never_throws()
    {
        var writer = new BackgroundWriter();
        using var gate = new ManualResetEventSlim();
        var written = false;

        var failing = writer.EnqueueAsync(() => throw new UnauthorizedAccessException());
        var waiting = writer.EnqueueAsync(() =>
        {
            Assert.True(gate.Wait(GateTimeout));
            written = true;
        });
        var flushed = writer.FlushAsync();

        Assert.False(flushed.IsCompleted);
        gate.Set();
        await flushed;
        Assert.True(written);
        await waiting;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => failing);
    }
}
