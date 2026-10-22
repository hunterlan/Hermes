namespace Hermes.Core.Models;

public enum KeyValueKind
{
    Text,
    File,
}

/// <summary>A row in a params / headers / form table.</summary>
public sealed class KeyValueItem
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Only meaningful for multipart form-data rows.</summary>
    public KeyValueKind Kind { get; set; } = KeyValueKind.Text;

    public KeyValueItem() { }

    public KeyValueItem(string key, string value, bool enabled = true)
    {
        Key = key;
        Value = value;
        Enabled = enabled;
    }
}
