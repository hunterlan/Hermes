using System.Globalization;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

public sealed class HistoryItemViewModel(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;
    public string Method => Entry.Request.Method;
    public string Url => Entry.Request.Url.Length == 0 ? "(no URL)" : Entry.Request.Url;
    public string Time => Entry.Timestamp.LocalDateTime.ToString("t", CultureInfo.CurrentCulture);
    public string Status => Entry.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "ERR";
    public bool IsSuccess => Entry.StatusCode is >= 200 and < 400;
    public bool IsError => !IsSuccess;
    public string Tooltip => $"{Entry.Request.Method} {Url}\n{Entry.Timestamp.LocalDateTime:g} · {Status} · {Entry.DurationMs} ms";
}
