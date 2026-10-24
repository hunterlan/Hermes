using System.Collections.Frozen;
using Hermes.Core.Models;

namespace Hermes.Core.Variables;

/// <summary>Collects variable layers for a <see cref="VariableScope"/>. Later layers win over earlier ones.</summary>
public sealed class VariableScopeBuilder
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _secretKeys = new(StringComparer.Ordinal);

    public VariableScopeBuilder AddLayer(IEnumerable<Variable>? variables)
    {
        if (variables is null)
            return this;
        foreach (var variable in variables)
        {
            if (!variable.Enabled || string.IsNullOrEmpty(variable.Key))
                continue;
            _values[variable.Key] = variable.EffectiveValue;
            if (variable.Type == VariableType.Secret)
                _secretKeys.Add(variable.Key);
            else
                _secretKeys.Remove(variable.Key);
        }
        return this;
    }

    public VariableScopeBuilder Set(string key, string value)
    {
        _values[key] = value;
        _secretKeys.Remove(key);
        return this;
    }

    /// <summary>A scope holding a copy of what was added so far; adding more afterwards doesn't change it.</summary>
    public VariableScope Build() =>
        new(_values.ToFrozenDictionary(StringComparer.Ordinal), _secretKeys.ToFrozenSet(StringComparer.Ordinal), dynamicValues: null,
            dynamicValuesUnresolved: false);
}
