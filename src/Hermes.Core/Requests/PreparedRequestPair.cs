using Hermes.Core.Http;

namespace Hermes.Core.Requests;

/// <summary>A request ready to send and its copy with secrets masked, as returned by <see cref="RequestPreparer.PrepareWithMasked"/>.</summary>
public sealed record PreparedRequestPair(PreparedRequest Request, PreparedRequest Masked);
