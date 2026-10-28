namespace Hermes.Core.Models;

public sealed class Variable
{
    public bool Enabled { get; set; } = true;
    public string Key { get; set; } = "";

    /// <summary>The "initial" value: shared, and the one written on export.</summary>
    public string Value { get; set; } = "";

    /// <summary>Local override; never exported. When null the initial value is used.</summary>
    public string? CurrentValue { get; set; }

    public VariableType Type { get; set; } = VariableType.Default;

    public string EffectiveValue => CurrentValue ?? Value;

    public Variable() { }

    public Variable(string key, string value)
    {
        Key = key;
        Value = value;
    }
}
