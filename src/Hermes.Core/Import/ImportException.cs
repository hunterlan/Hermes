namespace Hermes.Core.Import;

/// <summary>
/// Something is wrong with the text being imported, and the message says what in words the user can act on.
/// Importers throw it for problems in the file itself; any other exception from an importer is a Hermes bug.
/// It is a <see cref="FormatException"/> so callers that only care that the text isn't importable can catch that.
/// </summary>
public sealed class ImportException : FormatException
{
    /// <param name="problem">What is wrong, as a sentence without the final period.</param>
    /// <param name="path">
    /// Where in the file, as a JSON path such as <c>item[3].request.url</c>. Null when the problem already names the
    /// place, or the text has no structure to point into.
    /// </param>
    public ImportException(string problem, string? path = null)
        : base(path is null ? problem : $"{problem} (at {path})")
    {
        Problem = problem;
        Path = path;
    }

    public string Problem { get; }

    public string? Path { get; }
}
