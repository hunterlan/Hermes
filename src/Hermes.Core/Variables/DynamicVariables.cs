using System.Globalization;

namespace Hermes.Core.Variables;

/// <summary>Postman-style <c>{{$name}}</c> variables that produce a fresh value on every use.</summary>
public static class DynamicVariables
{
    private static readonly string[] FirstNames = ["Alex", "Maria", "John", "Olena", "Chen", "Fatima", "Lucas", "Emma"];
    private static readonly string[] LastNames = ["Smith", "Kovalenko", "Garcia", "Müller", "Wang", "Novak", "Brown"];
    private static readonly string[] Domains = ["example.com", "example.org", "mail.test"];

    public static IReadOnlyList<string> Names { get; } =
    [
        "$guid", "$randomUUID", "$timestamp", "$isoTimestamp", "$randomInt",
        "$randomBoolean", "$randomEmail", "$randomFirstName", "$randomLastName", "$randomAlphaNumeric",
    ];

    public static bool TryResolve(string name, out string value)
    {
        var rnd = Random.Shared;
        value = name switch
        {
            "$guid" or "$randomUUID" => Guid.NewGuid().ToString(),
            "$timestamp" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "$isoTimestamp" => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            "$randomInt" => rnd.Next(0, 1001).ToString(CultureInfo.InvariantCulture),
            "$randomBoolean" => rnd.Next(2) == 0 ? "false" : "true",
            "$randomFirstName" => FirstNames[rnd.Next(FirstNames.Length)],
            "$randomLastName" => LastNames[rnd.Next(LastNames.Length)],
            "$randomEmail" => $"{FirstNames[rnd.Next(FirstNames.Length)].ToLowerInvariant()}.{rnd.Next(1000)}@{Domains[rnd.Next(Domains.Length)]}",
            "$randomAlphaNumeric" => "abcdefghijklmnopqrstuvwxyz0123456789"[rnd.Next(36)].ToString(),
            _ => null!,
        };
        return value is not null;
    }
}
