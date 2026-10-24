using System.Text.RegularExpressions;

namespace Hermes.Core.Variables;

public static partial class VariableResolver
{
    private const int MaxDepth = 10;

    [GeneratedRegex(@"\{\{([^{}]+?)\}\}")]
    private static partial Regex VariablePattern();

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
        scope.TryGet(name, out _) || (IsDynamic(name) && DynamicVariables.Exists(name));

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
                if (scope.TryGet(name, out var value) || (IsDynamic(name) && scope.TryResolveDynamic(name, out value)))
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
