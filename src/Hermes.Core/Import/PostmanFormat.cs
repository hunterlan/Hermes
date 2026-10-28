using System.Globalization;
using System.Text.Json.Nodes;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;

namespace Hermes.Core.Import;

/// <summary>
/// The Postman names for Hermes values, and lenient readers for Postman JSON. Import and export both go through
/// here, so the two directions can't drift apart.
/// </summary>
internal static class PostmanFormat
{
    // ---- names ----

    private static readonly (OAuth2GrantType Grant, bool Pkce, string Name)[] GrantTypes =
    [
        (OAuth2GrantType.AuthorizationCode, false, "authorization_code"),
        (OAuth2GrantType.AuthorizationCode, true, "authorization_code_with_pkce"),
        (OAuth2GrantType.ClientCredentials, false, "client_credentials"),
        (OAuth2GrantType.Password, false, "password_credentials"),
    ];

    private static readonly (RawLanguage Language, string Name)[] RawLanguages =
    [
        (RawLanguage.Text, "text"),
        (RawLanguage.Json, "json"),
        (RawLanguage.Xml, "xml"),
        (RawLanguage.Html, "html"),
        (RawLanguage.JavaScript, "javascript"),
    ];

    public static string GrantTypeName(OAuth2GrantType grant, bool pkce) =>
        GrantTypes.First(g => g.Grant == grant && g.Pkce == (pkce && grant == OAuth2GrantType.AuthorizationCode)).Name;

    /// <summary>Unknown names fall back to the authorization code flow, Postman's default. "password" is an older name.</summary>
    public static (OAuth2GrantType Grant, bool Pkce) ParseGrantType(string? name) =>
        name == "password"
            ? (OAuth2GrantType.Password, false)
            : GrantTypes.FirstOrDefault(g => g.Name == name) is { Name: not null } match
                ? (match.Grant, match.Pkce)
                : (OAuth2GrantType.AuthorizationCode, false);

    public static string ClientAuthenticationName(OAuth2ClientAuthentication value) =>
        value == OAuth2ClientAuthentication.Body ? "body" : "header";

    public static OAuth2ClientAuthentication ParseClientAuthentication(string? name) =>
        name == "body" ? OAuth2ClientAuthentication.Body : OAuth2ClientAuthentication.Header;

    public static string ApiKeyLocationName(ApiKeyLocation value) => value == ApiKeyLocation.Query ? "query" : "header";

    public static ApiKeyLocation ParseApiKeyLocation(string? name) => name == "query" ? ApiKeyLocation.Query : ApiKeyLocation.Header;

    public static string VariableTypeName(VariableType value) => value == VariableType.Secret ? "secret" : "default";

    public static VariableType ParseVariableType(string? name) => name == "secret" ? VariableType.Secret : VariableType.Default;

    public static string RawLanguageName(RawLanguage language) => RawLanguages.First(l => l.Language == language).Name;

    /// <summary>Null when Postman gives no (or an unknown) language.</summary>
    public static RawLanguage? ParseRawLanguage(string? name) =>
        RawLanguages.FirstOrDefault(l => l.Name == name) is { Name: not null } match ? match.Language : null;

    // ---- lenient readers: hand-edited and generated files don't always use the JSON types the schema asks for ----

    public static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A value as text; non-string values keep their JSON form.</summary>
    public static string ValueString(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString(),
    };

    /// <summary>A flag written as a boolean, "true"/"false", or a number; null when absent or unreadable.</summary>
    public static bool? Flag(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<bool>(out var b))
            return b;
        if (v.TryGetValue<string>(out var s))
            return bool.TryParse(s.Trim(), out var parsed) ? parsed
                : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n != 0
                : null;
        return v.TryGetValue<double>(out var number) ? number != 0 : null;
    }

    /// <summary>Descriptions are either a string or {content, type}.</summary>
    public static string? Description(JsonNode? node) => node switch
    {
        JsonObject o => Str(o["content"]),
        _ => Str(node),
    };

    /// <summary>Collection variables mark disabled ones with "disabled"; environment values use "enabled".</summary>
    public static List<Variable> ReadVariables(JsonNode? node)
    {
        var list = new List<Variable>();
        if (node is not JsonArray array)
            return list;
        foreach (var v in array)
        {
            if (v is null)
                continue;
            list.Add(new Variable
            {
                Key = Str(v["key"]) ?? Str(v["id"]) ?? "",
                Value = ValueString(v["value"]),
                Enabled = Flag(v["disabled"]) != true && Flag(v["enabled"]) != false,
                Type = ParseVariableType(Str(v["type"])),
            });
        }
        return list;
    }
}
