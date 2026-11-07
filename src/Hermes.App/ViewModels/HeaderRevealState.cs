using Hermes.Core.Headers;

namespace Hermes.App.ViewModels;

/// <summary>
/// Which sensitive header values a tab has revealed. Header rows are rebuilt whenever the request is edited, so a
/// row can't hold this itself; new rows ask here and report changes back. The tab clears it when it is left, so
/// values are masked again when the user comes back.
/// </summary>
/// <param name="copyText">Copies text to the clipboard, for the copy buttons of the rows; null for none.</param>
public sealed class HeaderRevealState(Func<string, Task>? copyText)
{
    private readonly HashSet<string> _revealed = [];

    /// <summary>Creates the value state of a row in <paramref name="section"/>, revealed if it was before.</summary>
    public SensitiveValueViewModel CreateValue(string section, string headerName, string value, bool isSensitive)
    {
        var key = KeyOf(section, headerName);
        return new SensitiveValueViewModel(value, isSensitive, copyText)
        {
            IsRevealed = _revealed.Contains(key),
            RevealChanged = isRevealed =>
            {
                if (isRevealed)
                    _revealed.Add(key);
                else
                    _revealed.Remove(key);
            },
        };
    }

    /// <summary>Masks every value again.</summary>
    public void Clear() => _revealed.Clear();

    private static string KeyOf(string section, string headerName) =>
        $"{section}\n{headerName.Trim().ToUpperInvariant()}";
}
