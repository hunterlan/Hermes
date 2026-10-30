namespace Hermes.App.ViewModels;

/// <summary>A header that is sent but can't be edited in its table, with a short tag saying where it comes from.</summary>
public sealed class HeaderRowViewModel(string key, string value, string tagText)
{
    public string Key { get; } = key;
    public string Value { get; } = value;
    public string TagText { get; } = tagText;
}
