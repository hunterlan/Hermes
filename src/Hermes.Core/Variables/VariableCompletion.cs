namespace Hermes.Core.Variables;

/// <summary>Autocomplete for <c>{{variables}}</c> typed in an editor.</summary>
public static class VariableCompletion
{
    /// <summary>
    /// When the caret sits right after an unfinished <c>{{name</c>, returns where the name starts and what has been
    /// typed of it so far.
    /// </summary>
    public static bool TryFindPartialVariable(string? text, int caret, out int nameStart, out string typed)
    {
        nameStart = 0;
        typed = "";
        if (string.IsNullOrEmpty(text) || caret < 2 || caret > text.Length)
            return false;
        var open = text.LastIndexOf("{{", caret - 1, StringComparison.Ordinal);
        if (open < 0)
            return false;
        var start = open + 2;
        for (var i = start; i < caret; i++)
            if (text[i] is '{' or '}' || char.IsWhiteSpace(text[i]))
                return false;
        nameStart = start;
        typed = text[start..caret];
        return true;
    }

    /// <summary>
    /// Variable names matching <paramref name="typed"/>: prefix matches before substring matches, defined variables
    /// before dynamic ones, at most <paramref name="max"/> results.
    /// </summary>
    public static IReadOnlyList<VariableSuggestion> Suggest(VariableScope scope, string typed, int max = 5)
    {
        var candidates = scope.Values.Keys.Select(name => new VariableSuggestion(name, scope.TryGetDisplay(name, out var shown) ? shown : null))
            .Concat(DynamicVariables.Names.Where(n => !scope.Values.ContainsKey(n)).Select(n => new VariableSuggestion(n, null)));
        return candidates
            .Select(c => (Suggestion: c, Index: c.Name.IndexOf(typed, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c.Index >= 0)
            .OrderBy(c => c.Index == 0 ? 0 : 1)
            .ThenBy(c => c.Suggestion.Value is null ? 1 : 0)
            .ThenBy(c => c.Suggestion.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(c => c.Suggestion)
            .ToList();
    }
}
