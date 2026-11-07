namespace Hermes.Core.Http;

/// <summary>One header line of a raw request text.</summary>
public sealed record RawRequestHeader(string Name, string Value);
