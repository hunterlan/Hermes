using System.Text.RegularExpressions;
using Hermes.Core.Models;

namespace Hermes.Core.Secrets;

/// <summary>
/// Finds the secret values in a model so they can be kept out of the data files: values of Secret variables and
/// literal credentials in auth. An auth field that only references variables (<c>{{token}}</c>) is not a secret;
/// the variable it points at is.
/// </summary>
public static partial class SecretFields
{
    /// <summary>Takes the secrets out of <paramref name="collection"/> (blanking them) and returns them by slot.</summary>
    public static Dictionary<string, string> Strip(CollectionModel collection) => Strip(Slots(collection));

    public static Dictionary<string, string> Strip(EnvironmentModel environment) => Strip(Slots(environment.Variables, ""));

    public static Dictionary<string, string> Strip(SessionState session) => Strip(Slots(session));

    /// <summary>Blanks literal auth credentials. Used for history entries, which aren't restored.</summary>
    public static void Strip(HttpRequestModel request) => Strip(Slots(request.Auth, ""));

    /// <summary>Puts stored secrets back into fields the file left blank. A value present in the file wins.</summary>
    public static void Restore(CollectionModel collection, IReadOnlyDictionary<string, string> secrets) => Restore(Slots(collection), secrets);

    public static void Restore(EnvironmentModel environment, IReadOnlyDictionary<string, string> secrets) =>
        Restore(Slots(environment.Variables, ""), secrets);

    public static void Restore(SessionState session, IReadOnlyDictionary<string, string> secrets) => Restore(Slots(session), secrets);

    /// <summary>A copy of <paramref name="auth"/> with every credential field (literal or not) masked.</summary>
    public static AuthConfig Masked(AuthConfig auth)
    {
        var copy = Serialization.ModelCloner.Clone(auth);
        foreach (var slot in Slots(copy, ""))
            if (!string.IsNullOrEmpty(slot.Get()))
                slot.Set(Variables.VariableScope.MaskedValue);
        return copy;
    }

    /// <summary>True for a credential typed in as-is, false for one made only of <c>{{variable}}</c> references.</summary>
    public static bool IsLiteral(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !OnlyVariableReferences().IsMatch(value);

    [GeneratedRegex(@"^\s*(\{\{[^{}]+\}\}\s*)+$")]
    private static partial Regex OnlyVariableReferences();

    /// <summary>
    /// A secret field: where it lives and how to read and write it (null clears it). Variable slots always hold
    /// secrets; auth slots only when the value is literal.
    /// </summary>
    private sealed record Slot(string Name, Func<string?> Get, Action<string?> Set, bool IsAuth);

    private static Dictionary<string, string> Strip(IEnumerable<Slot> slots)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            var value = slot.Get();
            if (value is null || (slot.IsAuth ? !IsLiteral(value) : value.Length == 0))
                continue;
            secrets[slot.Name] = value;
            slot.Set(null);
        }
        return secrets;
    }

    private static void Restore(IEnumerable<Slot> slots, IReadOnlyDictionary<string, string> secrets)
    {
        if (secrets.Count == 0)
            return;
        foreach (var slot in slots)
            if (string.IsNullOrEmpty(slot.Get()) && secrets.TryGetValue(slot.Name, out var value))
                slot.Set(value);
    }

    private static IEnumerable<Slot> Slots(CollectionModel collection) =>
        Slots(collection.Variables, "")
            .Concat(Slots(collection.Auth, $"{collection.Id}/"))
            .Concat(Slots(collection.Items));

    private static IEnumerable<Slot> Slots(List<CollectionNode> items)
    {
        foreach (var node in items)
        {
            switch (node)
            {
                case FolderModel folder:
                    foreach (var slot in Slots(folder.Auth, $"{folder.Id}/").Concat(Slots(folder.Items)))
                        yield return slot;
                    break;
                case HttpRequestModel request:
                    foreach (var slot in Slots(request.Auth, $"{request.Id}/"))
                        yield return slot;
                    break;
            }
        }
    }

    private static IEnumerable<Slot> Slots(SessionState session) =>
        session.Tabs.SelectMany((tab, i) => tab.RequestDraft is { } draft ? Slots(draft.Auth, $"tab{i}/") : []);

    /// <summary>
    /// Secret variables, both the initial and the current value. Slots are named by key plus occurrence, so renaming a
    /// variable just moves its secret on the next save.
    /// </summary>
    private static IEnumerable<Slot> Slots(List<Variable> variables, string prefix)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            var occurrence = seen[variable.Key] = seen.GetValueOrDefault(variable.Key) + 1;
            if (variable.Type != VariableType.Secret)
                continue;
            var name = $"{prefix}var/{variable.Key}#{occurrence}";
            yield return new Slot(name, () => variable.Value, v => variable.Value = v ?? "", IsAuth: false);
            yield return new Slot(name + "/current", () => variable.CurrentValue, v => variable.CurrentValue = v, IsAuth: false);
        }
    }

    private static IEnumerable<Slot> Slots(AuthConfig auth, string prefix)
    {
        yield return new Slot(prefix + "basic/password", () => auth.Basic.Password, v => auth.Basic.Password = v ?? "", IsAuth: true);
        yield return new Slot(prefix + "bearer/token", () => auth.Bearer.Token, v => auth.Bearer.Token = v ?? "", IsAuth: true);
        yield return new Slot(prefix + "apiKey/value", () => auth.ApiKey.Value, v => auth.ApiKey.Value = v ?? "", IsAuth: true);
        var o = auth.OAuth2;
        yield return new Slot(prefix + "oauth2/clientSecret", () => o.ClientSecret, v => o.ClientSecret = v ?? "", IsAuth: true);
        yield return new Slot(prefix + "oauth2/password", () => o.Password, v => o.Password = v ?? "", IsAuth: true);
        yield return new Slot(prefix + "oauth2/accessToken", () => o.AccessToken, v => o.AccessToken = v ?? "", IsAuth: true);
        yield return new Slot(prefix + "oauth2/refreshToken", () => o.RefreshToken, v => o.RefreshToken = string.IsNullOrEmpty(v) ? null : v, IsAuth: true);
    }
}
