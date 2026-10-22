namespace Hermes.Core.Models;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public HttpRequestModel Request { get; set; } = new();
    public int? StatusCode { get; set; }
    public long DurationMs { get; set; }
}
