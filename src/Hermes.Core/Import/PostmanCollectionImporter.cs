using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Models;
using static Hermes.Core.Import.PostmanFormat;

namespace Hermes.Core.Import;

/// <summary>Reads Postman Collection Format v2.0 / v2.1.</summary>
public static class PostmanCollectionImporter
{
    public static bool LooksLikeCollection(JsonNode? root) =>
        root is JsonObject o && o["info"] is JsonObject info && o["item"] is JsonArray &&
        (Str(info["schema"])?.Contains("collection", StringComparison.OrdinalIgnoreCase) ?? true);

    public static CollectionModel Import(string json) =>
        Import(JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
               ?? throw new FormatException("Empty document."));

    public static CollectionModel Import(JsonNode root)
    {
        if (!LooksLikeCollection(root))
            throw new FormatException("Not a Postman collection (v2.0 / v2.1).");

        var info = root["info"]!;
        var collection = new CollectionModel
        {
            Name = Str(info["name"]) ?? "Imported collection",
            Description = Description(info["description"]),
            PostmanId = Str(info["_postman_id"]),
            Auth = ReadAuth(root["auth"]) ?? AuthConfig.NoAuth(),
            Variables = ReadVariables(root["variable"]),
            EventsJson = root["event"]?.ToJsonString(),
        };
        if (collection.Auth.Type == AuthType.Inherit)
            collection.Auth = AuthConfig.NoAuth();
        collection.Items.AddRange(ReadItems(root["item"]));
        return collection;
    }

    private static IEnumerable<CollectionNode> ReadItems(JsonNode? items)
    {
        if (items is not JsonArray array)
            yield break;
        foreach (var item in array)
        {
            if (item is null)
                continue;
            if (item["item"] is JsonArray)
            {
                var folder = new FolderModel
                {
                    Name = Str(item["name"]) ?? "Folder",
                    Description = Description(item["description"]),
                    Auth = ReadAuth(item["auth"]) ?? AuthConfig.Inherit(),
                    EventsJson = item["event"]?.ToJsonString(),
                };
                folder.Items.AddRange(ReadItems(item["item"]));
                yield return folder;
            }
            else
            {
                yield return ReadRequest(item);
            }
        }
    }

    private static HttpRequestModel ReadRequest(JsonNode item)
    {
        var model = new HttpRequestModel
        {
            Name = Str(item["name"]) ?? "Request",
            EventsJson = item["event"]?.ToJsonString(),
        };

        var request = item["request"];
        if (request is JsonValue)
        {
            // v2.1 allows "request": "https://..."
            model.Url = Str(request) ?? "";
            model.QueryParams = Http.UrlUtilities.ParseQuery(Http.UrlUtilities.Split(model.Url).Query);
            return model;
        }
        if (request is null)
            return model;

        model.Method = (Str(request["method"]) ?? "GET").ToUpperInvariant();
        model.Description = Description(request["description"]) ?? Description(item["description"]);
        model.Headers = ReadKeyValues(request["header"]);
        model.Auth = ReadAuth(request["auth"]) ?? AuthConfig.Inherit();
        ReadUrl(request["url"], model);
        ReadBody(request["body"], model);
        ReadProtocolProfile(item["protocolProfileBehavior"], model);
        return model;
    }

    private static void ReadUrl(JsonNode? url, HttpRequestModel model)
    {
        switch (url)
        {
            case JsonValue:
                model.Url = Str(url) ?? "";
                model.QueryParams = Http.UrlUtilities.ParseQuery(Http.UrlUtilities.Split(model.Url).Query);
                break;

            case JsonObject:
                var raw = Str(url["raw"]);
                if (raw is null)
                {
                    // Rebuild from parts when "raw" is missing.
                    var protocol = Str(url["protocol"]);
                    var host = url["host"] is JsonArray h ? string.Join('.', h.Select(Str)) : Str(url["host"]) ?? "";
                    var path = url["path"] is JsonArray p ? string.Join('/', p.Select(s => s is JsonObject o ? Str(o["value"]) : Str(s))) : Str(url["path"]) ?? "";
                    var port = Str(url["port"]);
                    raw = (protocol is null ? "" : protocol + "://") + host + (port is null ? "" : ":" + port) + (path.Length > 0 ? "/" + path : "");
                }

                var query = ReadKeyValues(url["query"]);
                if (query.Count > 0)
                {
                    // The structured query (with disabled entries) is authoritative.
                    model.QueryParams = query;
                    raw = Http.UrlUtilities.WithQuery(raw, query);
                }
                else
                {
                    model.QueryParams = Http.UrlUtilities.ParseQuery(Http.UrlUtilities.Split(raw).Query);
                }
                model.Url = raw;
                model.PathVariables = ReadKeyValues(url["variable"]);
                break;
        }
    }

    private static void ReadBody(JsonNode? body, HttpRequestModel model)
    {
        if (body is not JsonObject || Flag(body["disabled"]) == true)
            return;

        switch (Str(body["mode"]))
        {
            case "raw":
                model.Body.Mode = BodyMode.Raw;
                model.Body.Raw = Str(body["raw"]) ?? "";
                // "text" is also what Postman writes when nothing was chosen, so the Content-Type header decides then.
                model.Body.RawLanguage = ParseRawLanguage(Str(body["options"]?["raw"]?["language"])) is { } language and not RawLanguage.Text
                    ? language
                    : GuessLanguage(model.Headers);
                break;
            case "urlencoded":
                model.Body.Mode = BodyMode.UrlEncoded;
                model.Body.UrlEncoded = ReadKeyValues(body["urlencoded"]);
                break;
            case "formdata":
                model.Body.Mode = BodyMode.FormData;
                model.Body.FormData = ReadKeyValues(body["formdata"]);
                break;
            case "file":
                model.Body.Mode = BodyMode.Binary;
                model.Body.BinaryFilePath = Str(body["file"]?["src"]);
                break;
            default:
                model.Body.Mode = BodyMode.None;
                model.Body.UnsupportedJson = body.ToJsonString();
                break;
        }
    }

    private static RawLanguage GuessLanguage(List<KeyValueItem> headers)
    {
        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return RawLanguage.Json;
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)) return RawLanguage.Xml;
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return RawLanguage.Html;
        if (contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)) return RawLanguage.JavaScript;
        return RawLanguage.Text;
    }

    private static void ReadProtocolProfile(JsonNode? behavior, HttpRequestModel model)
    {
        if (behavior is not JsonObject)
            return;
        if (Flag(behavior["followRedirects"]) is { } follow)
            model.Settings.FollowRedirects = follow;
        if (Flag(behavior["strictSSL"]) is { } strict)
            model.Settings.VerifySsl = strict;
    }

    internal static AuthConfig? ReadAuth(JsonNode? auth)
    {
        if (auth is not JsonObject)
            return null;

        var type = Str(auth["type"]);
        var config = new AuthConfig();
        switch (type)
        {
            case "noauth":
                config.Type = AuthType.None;
                break;
            case "basic":
                var basic = Attributes(auth["basic"]);
                config.Type = AuthType.Basic;
                config.Basic.Username = basic.GetValueOrDefault("username", "");
                config.Basic.Password = basic.GetValueOrDefault("password", "");
                break;
            case "bearer":
                config.Type = AuthType.Bearer;
                config.Bearer.Token = Attributes(auth["bearer"]).GetValueOrDefault("token", "");
                break;
            case "apikey":
                var apikey = Attributes(auth["apikey"]);
                config.Type = AuthType.ApiKey;
                config.ApiKey.Key = apikey.GetValueOrDefault("key", "");
                config.ApiKey.Value = apikey.GetValueOrDefault("value", "");
                config.ApiKey.AddTo = ParseApiKeyLocation(apikey.GetValueOrDefault("in"));
                break;
            case "oauth2":
                var o = Attributes(auth["oauth2"]);
                config.Type = AuthType.OAuth2;
                (config.OAuth2.GrantType, config.OAuth2.UsePkce) = ParseGrantType(o.GetValueOrDefault("grant_type"));
                config.OAuth2.AuthUrl = o.GetValueOrDefault("authUrl", "");
                config.OAuth2.AccessTokenUrl = o.GetValueOrDefault("accessTokenUrl", "");
                config.OAuth2.ClientId = o.GetValueOrDefault("clientId", "");
                config.OAuth2.ClientSecret = o.GetValueOrDefault("clientSecret", "");
                config.OAuth2.Scope = o.GetValueOrDefault("scope", "");
                config.OAuth2.State = o.GetValueOrDefault("state", "");
                config.OAuth2.Username = o.GetValueOrDefault("username", "");
                config.OAuth2.Password = o.GetValueOrDefault("password", "");
                config.OAuth2.AccessToken = o.GetValueOrDefault("accessToken", "");
                config.OAuth2.HeaderPrefix = o.GetValueOrDefault("headerPrefix", "Bearer");
                if (o.TryGetValue("redirect_uri", out var redirect) && redirect.Length > 0)
                    config.OAuth2.RedirectUri = redirect;
                config.OAuth2.ClientAuthentication = ParseClientAuthentication(o.GetValueOrDefault("client_authentication"));
                break;
            default:
                // Unsupported auth types (digest, hawk, aws, ...) degrade to none.
                config.Type = AuthType.None;
                break;
        }
        return config;
    }

    /// <summary>v2.1 stores auth params as [{key, value}], v2.0 as a plain object.</summary>
    private static Dictionary<string, string> Attributes(JsonNode? node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (node)
        {
            case JsonArray array:
                foreach (var a in array)
                    if (Str(a?["key"]) is { } key)
                        result[key] = ValueString(a!["value"]);
                break;
            case JsonObject obj:
                foreach (var (key, value) in obj)
                    result[key] = ValueString(value);
                break;
        }
        return result;
    }

    private static List<KeyValueItem> ReadKeyValues(JsonNode? node)
    {
        var list = new List<KeyValueItem>();
        if (node is not JsonArray array)
            return list;
        foreach (var kv in array)
        {
            if (kv is not JsonObject)
                continue;
            var isFile = Str(kv["type"]) == "file";
            var value = isFile
                ? kv["src"] switch
                {
                    JsonArray srcs => srcs.Count > 0 ? Str(srcs[0]) ?? "" : "",
                    var src => Str(src) ?? "",
                }
                : ValueString(kv["value"]);
            list.Add(new KeyValueItem
            {
                Key = Str(kv["key"]) ?? "",
                Value = value,
                Enabled = Flag(kv["disabled"]) != true,
                Description = Description(kv["description"]),
                Kind = isFile ? KeyValueKind.File : KeyValueKind.Text,
            });
        }
        return list;
    }
}
