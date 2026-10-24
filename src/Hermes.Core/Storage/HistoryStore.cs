using System.Globalization;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>
/// Request history as one JSON Lines file per day (<c>yyyy-MM-dd.jsonl</c>), stored as given: callers strip
/// credentials before appending.
/// </summary>
public sealed class HistoryStore(string directory)
{
    private const int MaxEntriesPerDay = 500;
    private const string FileExtension = ".jsonl";

    private readonly Lock _lock = new();

    /// <summary>
    /// Entries from the last <paramref name="retentionDays"/> days, newest first; older files are deleted. Lines and
    /// files that can't be read are skipped and described in <paramref name="errors"/>.
    /// </summary>
    public List<HistoryEntry> Load(int retentionDays, ICollection<string> errors)
    {
        if (!Directory.Exists(directory))
            return [];

        var cutoff = DateTime.Today.AddDays(-Math.Max(1, retentionDays));
        var result = new List<HistoryEntry>();
        lock (_lock)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*" + FileExtension))
            {
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    continue;
                if (day < cutoff)
                {
                    File.Delete(file);
                    continue;
                }
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

    private string FileFor(DateTimeOffset timestamp) =>
        Path.Combine(directory, timestamp.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileExtension);
}
