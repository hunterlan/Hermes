using System.Text.RegularExpressions;

namespace Hermes.Core.Variables;

/// <summary>A layered set of variables. Later layers win over earlier ones (global &lt; collection &lt; environment).</summary>
public sealed class VariableScope
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public static VariableScope Empty { get; } = new();

    public VariableScope AddLayer(IEnumerable<Models.Variable>? variables)
    {
        if (variables is null)
            return this;
        foreach (var v in variables)
            if (v.Enabled && !string.IsNullOrEmpty(v.Key))
                _values[v.Key] = v.EffectiveValue;
        return this;
    }

    public VariableScope Set(string key, string value)
    {
        _values[key] = value;
        return this;
    }

    public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value!);

    public IReadOnlyDictionary<string, string> Values => _values;
}

public static partial class VariableResolver
{
    private const int MaxDepth = 10;

    [GeneratedRegex(@"\{\{([^{}]+?)\}\}")]
    private static partial Regex VariablePattern();

    public readonly record struct VariableMatch(int Start, int Length, string Name);

    /// <summary>Finds all <c>{{name}}</c> occurrences in <paramref name="text"/>.</summary>
    public static IEnumerable<VariableMatch> FindVariables(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;
        foreach (Match m in VariablePattern().Matches(text))
            yield return new VariableMatch(m.Index, m.Length, m.Groups[1].Value.Trim());
    }

    public static bool IsDynamic(string name) => name.StartsWith('$');

    /// <summary>Whether <paramref name="name"/> can be resolved, either from the scope or as a dynamic variable.</summary>
    public static bool CanResolve(string name, VariableScope scope) =>
        scope.TryGet(name, out _) || (IsDynamic(name) && DynamicVariables.TryResolve(name, out _));

    /// <summary>Replaces <c>{{name}}</c> with values from the scope. Unknown variables are left untouched.</summary>
    public static string Resolve(string? text, VariableScope scope)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
            return text ?? "";

        var current = text;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var changed = false;
            var next = VariablePattern().Replace(current, m =>
            {
                var name = m.Groups[1].Value.Trim();
                if (scope.TryGet(name, out var value) || (IsDynamic(name) && DynamicVariables.TryResolve(name, out value)))
                {
                    changed = true;
                    return value;
                }
                return m.Value;
            });
            current = next;
            if (!changed || !current.Contains("{{", StringComparison.Ordinal))
                break;
        }
        return current;
    }
}
