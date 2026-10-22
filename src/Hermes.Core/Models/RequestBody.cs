namespace Hermes.Core.Models;

public enum BodyMode
{
    None,
    FormData,
    UrlEncoded,
    Raw,
    Binary,
}

public enum RawLanguage
{
    Text,
    Json,
    Xml,
    Html,
    JavaScript,
}

public sealed class RequestBody
{
    public BodyMode Mode { get; set; } = BodyMode.None;
    public string Raw { get; set; } = "";
    public RawLanguage RawLanguage { get; set; } = RawLanguage.Json;
    public List<KeyValueItem> FormData { get; set; } = [];
    public List<KeyValueItem> UrlEncoded { get; set; } = [];
    public string? BinaryFilePath { get; set; }

    /// <summary>
    /// Body of a mode Hermes doesn't support (e.g. GraphQL from a Postman import), kept verbatim
    /// so exporting doesn't lose it.
    /// </summary>
    public string? UnsupportedJson { get; set; }

    public static string ContentTypeFor(RawLanguage language) => language switch
    {
        RawLanguage.Json => "application/json",
        RawLanguage.Xml => "application/xml",
        RawLanguage.Html => "text/html",
        RawLanguage.JavaScript => "application/javascript",
        _ => "text/plain",
    };
}
