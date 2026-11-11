namespace Hermes.Core.Models;

/// <summary>One labelled line in the Details part of a failure, e.g. "Issued by" and the authority's name.</summary>
public sealed record FailureDetail(string Label, string Value);
