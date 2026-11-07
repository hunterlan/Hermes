namespace Hermes.Core.Headers;

/// <summary>The request, folder or collection a header is set on.</summary>
/// <param name="Id">Id of the request, folder or collection, so the UI can open it.</param>
public sealed record HeaderSource(HeaderSourceKind Kind, Guid Id, string Name);
