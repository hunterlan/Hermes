namespace Hermes.Core.Variables;

/// <summary>A layered set of variables. Later layers win over earlier ones (global &lt; collection &lt; environment).</summary>
public sealed class VariableScope
{
    /// <summary>Shown (and saved) in place of a secret value.</summary>
    public const string MaskedValue = "••••••";

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _secretKeys = new(StringComparer.Ordinal);
    private DynamicValueLog? _dynamicValues;

    public static VariableScope Empty { get; } = new();

    public VariableScope AddLayer(IEnumerable<Models.Variable>? variables)
    {
        if (variables is null)
            return this;
        foreach (var v in variables)
        {
            if (!v.Enabled || string.IsNullOrEmpty(v.Key))
                continue;
            _values[v.Key] = v.EffectiveValue;
            if (v.Type == Models.VariableType.Secret)
                _secretKeys.Add(v.Key);
            else
                _secretKeys.Remove(v.Key);
        }
        return this;
    }

    public VariableScope Set(string key, string value)
    {
        _values[key] = value;
        _secretKeys.Remove(key);
        return this;
    }

    /// <summary>Replaces every secret value with <see cref="MaskedValue"/>, for a copy of a request that is shown or saved.</summary>
    public VariableScope MaskSecrets()
    {
        foreach (var key in _secretKeys)
            _values[key] = MaskedValue;
        return this;
    }

    /// <summary>Takes dynamic values (<c>{{$guid}}</c>, ...) from <paramref name="log"/> instead of generating them freely.</summary>
    public VariableScope UseDynamicValues(DynamicValueLog log)
    {
        _dynamicValues = log;
        return this;
    }

    public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value!);

    public bool IsSecret(string key) => _secretKeys.Contains(key);

    /// <summary>The value to show for <paramref name="key"/>: <see cref="MaskedValue"/> when it's a secret.</summary>
    public bool TryGetDisplay(string key, out string value)
    {
        if (!_values.TryGetValue(key, out value!))
            return false;
        if (IsSecret(key))
            value = MaskedValue;
        return true;
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    internal bool TryResolveDynamic(string name, out string value) =>
        _dynamicValues is { } log ? log.TryResolve(name, out value) : DynamicVariables.TryResolve(name, out value);
}
