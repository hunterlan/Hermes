using System.Globalization;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>
/// Request history as one JSON Lines file per day (<c>yyyy-MM-dd.jsonl</c>), stored as given: callers strip
/// credentials before appending.
/// </summary>
public sealed class HistoryStore(string directory, TimeProvider? timeProvider = null)
{
    private const int MaxEntriesPerDay = 500;
    private const string FileExtension = ".jsonl";
    private const string DayFormat = "yyyy-MM-dd";

    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// All stored entries, newest first. Lines and files that can't be read are skipped and described in
    /// <paramref name="errors"/>. Doesn't remove expired days; see <see cref="Prune"/>.
    /// </summary>
    public List<HistoryEntry> Load(ICollection<string> errors)
    {
        if (!Directory.Exists(directory))
            return [];

        var result = new List<HistoryEntry>();
        lock (_lock)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*" + FileExtension))
            {
                if (!TryParseDay(file, out _))
                    continue;
                try
                {
                    var lineErrors = new List<string>();
                    var entries = JsonFileStore.ReadLines(file, HermesJsonContext.Default.HistoryEntry, lineErrors);
                    foreach (var error in lineErrors)
                        errors.Add(error);
                    // Appends don't trim, so a busy day is cut back to its newest entries here. Lines that couldn't
                    // be read (cut short by a crash) are dropped at the same time, so they're reported only once.
                    var trimmed = entries.Count > MaxEntriesPerDay;
                    if (trimmed)
                        entries.RemoveRange(0, entries.Count - MaxEntriesPerDay);
                    if (trimmed || lineErrors.Count > 0)
                        JsonFileStore.WriteLines(file, entries, HermesJsonContext.Default.HistoryEntry);
                    result.AddRange(entries);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{file}: {ex.Message}");
                }
            }
        }
        return result.OrderByDescending(h => h.Timestamp).ToList();
    }

    /// <summary>
    /// Deletes the files of days older than the last <paramref name="retentionDays"/> days (today included). Does
    /// nothing for <see cref="HistoryRetention.KeepForever"/>.
    /// </summary>
    public void Prune(int retentionDays)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (HistoryRetention.OldestKeptDay(retentionDays, today) is not { } oldestKeptDay || !Directory.Exists(directory))
            return;

        lock (_lock)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*" + FileExtension))
                if (TryParseDay(file, out var day) && day < oldestKeptDay)
                    File.Delete(file);
        }
    }

    public void Append(HistoryEntry entry)
    {
        lock (_lock)
            JsonFileStore.AppendLine(FileFor(entry.Timestamp), entry, HermesJsonContext.Default.HistoryEntry);
    }

    public void Delete(HistoryEntry entry)
    {
        lock (_lock)
        {
            var file = FileFor(entry.Timestamp);
            if (!File.Exists(file))
                return;
            // Lines that can't be read are dropped by the rewrite; they were already reported when history loaded.
            var entries = JsonFileStore.ReadLines(file, HermesJsonContext.Default.HistoryEntry, []);
            entries.RemoveAll(e => e.Id == entry.Id);
            JsonFileStore.WriteLines(file, entries, HermesJsonContext.Default.HistoryEntry);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static bool TryParseDay(string file, out DateOnly day) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), DayFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out day);

    private string FileFor(DateTimeOffset timestamp) =>
        Path.Combine(directory, timestamp.LocalDateTime.ToString(DayFormat, CultureInfo.InvariantCulture) + FileExtension);
}
