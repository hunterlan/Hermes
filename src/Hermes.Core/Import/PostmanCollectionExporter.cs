using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Http;
using Hermes.Core.Models;
using static Hermes.Core.Import.PostmanFormat;

namespace Hermes.Core.Import;

/// <summary>Writes Postman Collection Format v2.1.0.</summary>
public static class PostmanCollectionExporter
{
    public const string SchemaUrl = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Export(CollectionModel collection)
    {
        var root = new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["_postman_id"] = collection.PostmanId ?? collection.Id.ToString(),
                ["name"] = collection.Name,
                ["description"] = collection.Description,
                ["schema"] = SchemaUrl,
            },
            ["item"] = WriteItems(collection.Items),
        };
        if (WriteAuth(collection.Auth) is { } auth)
            root["auth"] = auth;
        if (ParseEvents(collection.EventsJson) is { } events)
            root["event"] = events;
        if (collection.Variables.Count > 0)
            root["variable"] = WriteVariables(collection.Variables);

        RemoveNulls(root);
        return root.ToJsonString(WriteOptions);
    }

    private static JsonArray WriteItems(IEnumerable<CollectionNode> items)
    {
        var array = new JsonArray();
        foreach (var node in items)
        {
            switch (node)
            {
                case FolderModel folder:
                    var f = new JsonObject
                    {
                        ["name"] = folder.Name,
                        ["description"] = folder.Description,
                        ["item"] = WriteItems(folder.Items),
                    };
                    if (WriteAuth(folder.Auth) is { } folderAuth)
                        f["auth"] = folderAuth;
                    if (ParseEvents(folder.EventsJson) is { } folderEvents)
                        f["event"] = folderEvents;
                    array.Add(f);
                    break;

                case HttpRequestModel request:
                    array.Add(WriteRequestItem(request));
                    break;
            }
        }
        return array;
    }

    private static JsonObject WriteRequestItem(HttpRequestModel r)
    {
        var request = new JsonObject
        {
            ["method"] = r.Method,
            ["header"] = WriteKeyValues(r.Headers),
        };
        if (WriteAuth(r.Auth) is { } auth)
            request["auth"] = auth;
        if (WriteBody(r.Body) is { } body)
            request["body"] = body;
        request["url"] = WriteUrl(r);
        if (r.Description is not null)
            request["description"] = r.Description;

        var item = new JsonObject
        {
            ["name"] = r.Name,
        };
        if (ParseEvents(r.EventsJson) is { } events)
            item["event"] = events;

        var behavior = new JsonObject();
        if (r.Settings.FollowRedirects is { } follow)
            behavior["followRedirects"] = follow;
        if (r.Settings.VerifySsl is { } strict)
            behavior["strictSSL"] = strict;
        if (behavior.Count > 0)
            item["protocolProfileBehavior"] = behavior;

        item["request"] = request;
        item["response"] = new JsonArray();
        return item;
    }

    private static JsonObject WriteUrl(HttpRequestModel r)
    {
        var raw = r.Url;
        var url = new JsonObject { ["raw"] = raw };
        var parts = UrlUtilities.Split(raw);

        var schemeEnd = parts.Base.IndexOf("://", StringComparison.Ordinal);
        var rest = parts.Base;
        if (schemeEnd >= 0)
        {
            url["protocol"] = parts.Base[..schemeEnd];
            rest = parts.Base[(schemeEnd + 3)..];
        }
        var slash = rest.IndexOf('/');
        var hostPort = slash < 0 ? rest : rest[..slash];
        var path = slash < 0 ? "" : rest[(slash + 1)..];

        var colon = hostPort.LastIndexOf(':');
        if (colon > 0 && !hostPort.EndsWith("}}", StringComparison.Ordinal) && int.TryParse(hostPort[(colon + 1)..], out _))
        {
            url["port"] = hostPort[(colon + 1)..];
            hostPort = hostPort[..colon];
        }
        url["host"] = new JsonArray(hostPort.Split('.').Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        if (path.Length > 0)
            url["path"] = new JsonArray(path.Split('/').Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        if (r.QueryParams.Count > 0)
            url["query"] = WriteKeyValues(r.QueryParams);
        if (r.PathVariables.Count > 0)
            url["variable"] = new JsonArray(r.PathVariables.Select(v => (JsonNode?)new JsonObject
            {
                ["key"] = v.Key,
                ["value"] = v.Value,
                ["description"] = v.Description,
            }).ToArray());
        return url;
    }

    private static JsonObject? WriteBody(RequestBody body)
    {
        switch (body.Mode)
        {
            case BodyMode.Raw:
                return new JsonObject
                {
                    ["mode"] = "raw",
                    ["raw"] = body.Raw,
                    ["options"] = new JsonObject
                    {
                        ["raw"] = new JsonObject { ["language"] = RawLanguageName(body.RawLanguage) },
                    },
                };
            case BodyMode.UrlEncoded:
                return new JsonObject { ["mode"] = "urlencoded", ["urlencoded"] = WriteKeyValues(body.UrlEncoded) };
            case BodyMode.FormData:
                return new JsonObject { ["mode"] = "formdata", ["formdata"] = WriteKeyValues(body.FormData, formData: true) };
            case BodyMode.Binary:
                return new JsonObject { ["mode"] = "file", ["file"] = new JsonObject { ["src"] = body.BinaryFilePath } };
            default:
                return body.UnsupportedJson is { } json ? JsonNode.Parse(json) as JsonObject : null;
        }
    }

    internal static JsonObject? WriteAuth(AuthConfig auth)
    {
        static JsonArray Attrs(params (string Key, object? Value)[] attrs)
        {
            var array = new JsonArray();
            foreach (var (key, value) in attrs)
            {
                var node = value switch
                {
                    bool b => JsonValue.Create(b),
                    _ => (JsonNode?)JsonValue.Create(value?.ToString() ?? ""),
                };
                array.Add(new JsonObject { ["key"] = key, ["value"] = node, ["type"] = value is bool ? "boolean" : "string" });
            }
            return array;
        }

        return auth.Type switch
        {
            AuthType.Inherit => null,
            AuthType.None => new JsonObject { ["type"] = "noauth" },
            AuthType.Basic => new JsonObject
            {
                ["type"] = "basic",
                ["basic"] = Attrs(("username", auth.Basic.Username), ("password", auth.Basic.Password)),
            },
            AuthType.Bearer => new JsonObject
            {
                ["type"] = "bearer",
                ["bearer"] = Attrs(("token", auth.Bearer.Token)),
            },
            AuthType.ApiKey => new JsonObject
            {
                ["type"] = "apikey",
                ["apikey"] = Attrs(("key", auth.ApiKey.Key), ("value", auth.ApiKey.Value),
                    ("in", ApiKeyLocationName(auth.ApiKey.AddTo))),
            },
            AuthType.OAuth2 => new JsonObject
            {
                ["type"] = "oauth2",
                ["oauth2"] = Attrs(
                    ("grant_type", GrantTypeName(auth.OAuth2.GrantType, auth.OAuth2.UsePkce)),
                    ("authUrl", auth.OAuth2.AuthUrl),
                    ("accessTokenUrl", auth.OAuth2.AccessTokenUrl),
                    ("clientId", auth.OAuth2.ClientId),
                    ("clientSecret", auth.OAuth2.ClientSecret),
                    ("scope", auth.OAuth2.Scope),
                    ("state", auth.OAuth2.State),
                    ("redirect_uri", auth.OAuth2.RedirectUri),
                    ("username", auth.OAuth2.Username),
                    ("password", auth.OAuth2.Password),
                    ("accessToken", auth.OAuth2.AccessToken),
                    ("headerPrefix", auth.OAuth2.HeaderPrefix),
                    ("client_authentication", ClientAuthenticationName(auth.OAuth2.ClientAuthentication)),
                    ("addTokenTo", "header")),
            },
            _ => null,
        };
    }

    private static JsonArray WriteVariables(IEnumerable<Variable> variables) =>
        new(variables.Select(v =>
        {
            var o = new JsonObject
            {
                ["key"] = v.Key,
                ["value"] = v.Value,
                ["type"] = VariableTypeName(v.Type),
            };
            if (!v.Enabled)
                o["disabled"] = true;
            return (JsonNode?)o;
        }).ToArray());

    private static JsonArray WriteKeyValues(IEnumerable<KeyValueItem> items, bool formData = false) =>
        new(items.Select(i =>
        {
            var o = new JsonObject { ["key"] = i.Key };
            if (formData && i.Kind == KeyValueKind.File)
            {
                o["type"] = "file";
                o["src"] = i.Value;
            }
            else
            {
                o["value"] = i.Value;
                if (formData)
                    o["type"] = "text";
            }
            if (i.Description is not null)
                o["description"] = i.Description;
            if (!i.Enabled)
                o["disabled"] = true;
            return (JsonNode?)o;
        }).ToArray());

    private static JsonNode? ParseEvents(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void RemoveNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Where(p => p.Value is null).Select(p => p.Key).ToList())
                    o.Remove(key);
                foreach (var (_, value) in o)
                    RemoveNulls(value);
                break;
            case JsonArray a:
                foreach (var child in a)
                    RemoveNulls(child);
                break;
        }
    }
}
