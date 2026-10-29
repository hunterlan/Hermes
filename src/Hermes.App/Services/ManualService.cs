using System.Text.RegularExpressions;
using Avalonia.Platform;

namespace Hermes.App.Services;

/// <summary>Reads the Markdown manual that is embedded in the app (see docs/manual).</summary>
public sealed partial class ManualService
{
    public const string IndexFileName = "index.md";

    private const string ResourceRoot = "avares://Hermes/Assets/manual/";

    private IReadOnlyList<ManualPage>? _pages;

    /// <summary>The pages in the order the index lists them, without the index itself.</summary>
    public IReadOnlyList<ManualPage> Pages => _pages ??= ParseIndex(Read(IndexFileName));

    public bool Exists(string fileName) => AssetLoader.Exists(new Uri(ResourceRoot + fileName));

    public string Read(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri(ResourceRoot + fileName));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<ManualPage> ParseIndex(string indexMarkdown) =>
        PageLink().Matches(indexMarkdown)
            .Select(match => new ManualPage(match.Groups["title"].Value, match.Groups["file"].Value))
            .ToList();

    [GeneratedRegex(@"\[(?<title>[^\]]+)\]\((?<file>[^)#:/]+\.md)\)")]
    private static partial Regex PageLink();
}
