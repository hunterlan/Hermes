using System.Collections.Frozen;
using System.Diagnostics.Contracts;

namespace Hermes.Core.Variables;

/// <summary>
/// A layered set of variables (global &lt; collection &lt; environment), built with <see cref="VariableScopeBuilder"/>.
/// Never changes once built: the <c>With…</c> methods return a new scope.
/// </summary>
public sealed class VariableScope
{
    /// <summary>Shown (and saved) in place of a secret value.</summary>
    public const string MaskedValue = "••••••";

    private readonly FrozenDictionary<string, string> _values;
    private readonly FrozenSet<string> _secretKeys;
    private readonly DynamicValueLog? _dynamicValues;
    private readonly bool _dynamicValuesUnresolved;

    internal VariableScope(FrozenDictionary<string, string> values, FrozenSet<string> secretKeys, DynamicValueLog? dynamicValues,
        bool dynamicValuesUnresolved)
    {
        _values = values;
        _secretKeys = secretKeys;
        _dynamicValues = dynamicValues;
        _dynamicValuesUnresolved = dynamicValuesUnresolved;
    }

    public static VariableScope Empty { get; } = new VariableScopeBuilder().Build();

    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>A copy with every secret value replaced by <see cref="MaskedValue"/>, for a request that is shown or saved.</summary>
    [Pure]
    public VariableScope WithSecretsMasked() =>
        new(_values.ToFrozenDictionary(entry => entry.Key, entry => IsSecret(entry.Key) ? MaskedValue : entry.Value, StringComparer.Ordinal),
            _secretKeys, _dynamicValues, _dynamicValuesUnresolved);

    /// <summary>A copy that takes dynamic values (<c>{{$guid}}</c>, ...) from <paramref name="log"/> instead of generating them freely.</summary>
    [Pure]
    public VariableScope WithDynamicValues(DynamicValueLog log) => new(_values, _secretKeys, log, dynamicValuesUnresolved: false);

    /// <summary>
    /// A copy that leaves dynamic variables (<c>{{$guid}}</c>, ...) as written, for a preview of a request that isn't
    /// sent: values generated for it would differ from the ones the request is sent with.
    /// </summary>
    [Pure]
    public VariableScope WithDynamicValuesUnresolved() => new(_values, _secretKeys, dynamicValues: null, dynamicValuesUnresolved: true);

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

    internal bool TryResolveDynamic(string name, out string value)
    {
        if (_dynamicValuesUnresolved)
        {
            value = "";
            return false;
        }
        return _dynamicValues is { } log ? log.TryResolve(name, out value) : DynamicVariables.TryResolve(name, out value);
    }
}
