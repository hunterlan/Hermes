namespace Hermes.Core.Headers;

/// <summary>What happens to a header set on the request, a folder or the collection.</summary>
public enum HeaderState
{
    Sent,

    /// <summary>Inherited, but the request skips headers of this name.</summary>
    Skipped,

    /// <summary>The request or an inner folder sets a header of the same name.</summary>
    Overridden,
}
