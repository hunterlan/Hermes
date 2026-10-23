namespace Hermes.Core.Models;

/// <summary>A request's saved responses (newest first), and the files among them that couldn't be read.</summary>
public sealed record SavedResponses(IReadOnlyList<SavedResponse> Items, IReadOnlyList<string> Errors);
