namespace Hermes.Core.Web;

public readonly record struct UrlParts(string Base, string? Query, string? Fragment);
