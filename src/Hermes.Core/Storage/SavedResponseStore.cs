using System.Globalization;
using System.Text.Json;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>Responses saved per request, one file each: <c>{requestId}/{sequence}.json</c>.</summary>
public sealed class SavedResponseStore(string directory)
{
    /// <summary>The latest response plus five earlier ones are kept per request.</summary>
    public const int MaxResponsesPerRequest = 6;

    /// <summary>Bodies above this are not stored with saved responses.</summary>
    public const long MaxBodyBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Saved responses for a request, newest first. Files that can't be read are skipped and described in
    /// <see cref="SavedResponses.Errors"/>; they are left on disk, never overwritten.
    /// </summary>
    public SavedResponses Load(Guid requestId)
    {
        var items = new List<SavedResponse>();
        var errors = new List<string>();
        List<string> files;
        try
        {
            files = FilesFor(requestId).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SavedResponses(items, [$"{DirectoryFor(requestId)}: {ex.Message}"]);
        }
        foreach (var file in files)
        {
            try
            {
                if (JsonFileStore.Read(file, HermesJsonContext.Default.SavedResponse) is { } saved)
                    items.Add(saved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }
        return new SavedResponses(items, errors);
    }

    /// <summary>
    /// Stores a response in a file of its own and removes those beyond the newest <see cref="MaxResponsesPerRequest"/>.
    /// Returns what was stored: without the body when it's larger than <see cref="MaxBodyBytes"/>.
    /// </summary>
    public SavedResponse Save(Guid requestId, SavedResponse saved)
    {
        if (saved.Response.BodySize > MaxBodyBytes)
        {
            saved = new SavedResponse
            {
                Timestamp = saved.Timestamp,
                SentRequest = saved.SentRequest,
                BodyOmitted = true,
                Response = WithoutBody(saved.Response),
            };
        }
        // Names sort by save order, so the newest files are found without reading any of them. The sequence number
        // is the timestamp, moved past the newest existing file when two saves land on the same tick.
        var newest = FilesFor(requestId).Select(SequenceOf).FirstOrDefault();
        var sequence = Math.Max(saved.Timestamp.UtcTicks, newest + 1);
        JsonFileStore.Write(Path.Combine(DirectoryFor(requestId), $"{sequence:D19}.json"), saved,
            HermesJsonContext.Default.SavedResponse);
        Prune(requestId);
        return saved;
    }

    public void Delete(Guid requestId)
    {
        var dir = DirectoryFor(requestId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Removes saved responses beyond the newest <see cref="MaxResponsesPerRequest"/>. Best effort: the save itself
    /// has succeeded, and a file that can't be removed now (held open by a scanner) is removed after a later save.
    /// </summary>
    private void Prune(Guid requestId)
    {
        try
        {
            foreach (var old in FilesFor(requestId).Skip(MaxResponsesPerRequest))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string DirectoryFor(Guid requestId) => Path.Combine(directory, requestId.ToString());

    private static long SequenceOf(string file) =>
        long.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>The request's saved-response files, newest first.</summary>
    private IEnumerable<string> FilesFor(Guid requestId)
    {
        var dir = DirectoryFor(requestId);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            : [];
    }

    private static ResponseModel WithoutBody(ResponseModel r) => new()
    {
        StatusCode = r.StatusCode,
        ReasonPhrase = r.ReasonPhrase,
        HttpVersion = r.HttpVersion,
        Headers = r.Headers,
        ContentType = r.ContentType,
        Charset = r.Charset,
        FinalUri = r.FinalUri,
        TimeToFirstByte = r.TimeToFirstByte,
        TotalTime = r.TotalTime,
        HeadersSize = r.HeadersSize,
        Cookies = r.Cookies,
    };
}
