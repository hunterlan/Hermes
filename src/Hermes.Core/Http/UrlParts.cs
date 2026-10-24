namespace Hermes.Core.Http;

public readonly record struct UrlParts(string Base, string? Query, string? Fragment);
