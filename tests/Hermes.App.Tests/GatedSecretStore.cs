using System.Collections.Concurrent;
using Hermes.Core.Secrets;

namespace Hermes.App.Tests;

/// <summary>
/// A secret store that stands in for a keychain that can block (on macOS it may wait for a permission prompt): each
/// write records the thread it ran on and waits until <see cref="Gate"/> is open.
/// </summary>
internal sealed class GatedSecretStore : SecretStore
{
    // A write that waits this long would hang the test run instead of failing it.
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private byte[] _data = [];

    /// <summary>Writes wait while this is reset. Open from the start.</summary>
    public ManualResetEventSlim Gate { get; } = new(initialState: true);

    /// <summary>Released once for each write that has started (and may now be waiting at the gate).</summary>
    public SemaphoreSlim WriteStarted { get; } = new(0);

    /// <summary>The managed thread id of each finished write, in order.</summary>
    public ConcurrentQueue<int> WriteThreads { get; } = new();

    protected override byte[] Read() => _data;

    protected override void Write(byte[] data)
    {
        WriteStarted.Release();
        if (!Gate.Wait(GateTimeout))
            throw new TimeoutException("The gate stayed closed: the write was waiting on the thread that should open it.");
        _data = data;
        WriteThreads.Enqueue(Environment.CurrentManagedThreadId);
    }
}
