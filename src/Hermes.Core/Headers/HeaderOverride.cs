using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>A request that overrides some headers of an outer container.</summary>
public sealed record HeaderOverride(HttpRequestModel Request, IReadOnlyList<string> HeaderNames);
