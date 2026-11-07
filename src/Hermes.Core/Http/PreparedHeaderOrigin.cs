namespace Hermes.Core.Http;

/// <summary>Why a header is on a <see cref="PreparedRequest"/>.</summary>
public enum PreparedHeaderOrigin
{
    /// <summary>Set by the user on the request, a folder or the collection.</summary>
    Configured,

    /// <summary>Added by the effective auth.</summary>
    Auth,

    /// <summary>Added for the body (Content-Type).</summary>
    Body,

    /// <summary>One of <see cref="Requests.RequestPreparer.DefaultHeaders"/>.</summary>
    Default,
}
