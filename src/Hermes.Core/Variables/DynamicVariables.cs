using System.Globalization;

namespace Hermes.Core.Variables;

/// <summary>Postman-style <c>{{$name}}</c> variables that produce a fresh value on every use.</summary>
public static class DynamicVariables
{
    private static readonly string[] FirstNames = ["Alex", "Maria", "John", "Olena", "Chen", "Fatima", "Lucas", "Emma"];
    private static readonly string[] LastNames = ["Smith", "Kovalenko", "Garcia", "Müller", "Wang", "Novak", "Brown"];
    private static readonly string[] Domains = ["example.com", "example.org", "mail.test"];

    // One table, so the names offered and checked can't drift from the ones that resolve.
    private static readonly Dictionary<string, Func<Random, string>> Generators = new(StringComparer.Ordinal)
    {
        ["$guid"] = _ => Guid.NewGuid().ToString(),
        ["$randomUUID"] = _ => Guid.NewGuid().ToString(),
        ["$timestamp"] = _ => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        ["$isoTimestamp"] = _ => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        ["$randomInt"] = rnd => rnd.Next(0, 1001).ToString(CultureInfo.InvariantCulture),
        ["$randomBoolean"] = rnd => rnd.Next(2) == 0 ? "false" : "true",
        ["$randomEmail"] = rnd => $"{FirstNames[rnd.Next(FirstNames.Length)].ToLowerInvariant()}.{rnd.Next(1000)}@{Domains[rnd.Next(Domains.Length)]}",
        ["$randomFirstName"] = rnd => FirstNames[rnd.Next(FirstNames.Length)],
        ["$randomLastName"] = rnd => LastNames[rnd.Next(LastNames.Length)],
        ["$randomAlphaNumeric"] = rnd => "abcdefghijklmnopqrstuvwxyz0123456789"[rnd.Next(36)].ToString(),
    };

    public static IReadOnlyCollection<string> Names => Generators.Keys;

    public static bool Exists(string name) => Generators.ContainsKey(name);

    public static bool TryResolve(string name, out string value)
    {
        value = Generators.TryGetValue(name, out var generate) ? generate(Random.Shared) : null!;
        return value is not null;
    }
}
