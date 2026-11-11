using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hermes.Core.Http;

/// <summary>
/// Remembers the certificate facts of the latest failed validation per host. The handshake runs on a pooled
/// connection, not on the caller's flow, so the validation callback can't hand its facts to the request directly;
/// the executor reads them from here once the request has failed.
/// </summary>
public sealed class CertificateFailureLog
{
    /// <summary>More hosts than this and the oldest entries are dropped, so the log can't grow without limit.</summary>
    private const int MaxHosts = 64;

    private readonly ConcurrentDictionary<string, (CertificateFacts Facts, long Timestamp)> _byHost = new(StringComparer.OrdinalIgnoreCase);

    public void Record(CertificateFacts facts)
    {
        _byHost[facts.Host] = (facts, Stopwatch.GetTimestamp());
        if (_byHost.Count <= MaxHosts)
            return;

        foreach (var oldest in _byHost.OrderBy(pair => pair.Value.Timestamp).Take(_byHost.Count - MaxHosts).Select(pair => pair.Key))
            _byHost.TryRemove(oldest, out _);
    }

    /// <summary>A validation that passed: whatever was recorded for the host is out of date.</summary>
    public void Clear(string host) => _byHost.TryRemove(host, out _);

    /// <summary>
    /// The facts recorded for <paramref name="host"/> since <paramref name="sinceTimestamp"/> (a
    /// <see cref="Stopwatch.GetTimestamp"/> value). When there are none, the newest failure recorded since then,
    /// which is the case when a redirect led the request to another host. Nothing is removed, so requests failing
    /// together on one host all find the facts; a later successful validation clears them.
    /// </summary>
    public CertificateFacts? Find(string host, long sinceTimestamp)
    {
        if (_byHost.TryGetValue(host, out var entry) && entry.Timestamp >= sinceTimestamp)
            return entry.Facts;

        return _byHost.Values.Where(recorded => recorded.Timestamp >= sinceTimestamp)
            .OrderByDescending(recorded => recorded.Timestamp)
            .Select(recorded => recorded.Facts)
            .FirstOrDefault();
    }
}
