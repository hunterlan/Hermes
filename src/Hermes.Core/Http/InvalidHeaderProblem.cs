namespace Hermes.Core.Http;

/// <summary>Why a header can't be sent.</summary>
public enum InvalidHeaderProblem
{
    /// <summary>The name has characters a header name can't have, such as a space.</summary>
    Name,

    /// <summary>.NET refuses the value for this header.</summary>
    Value,

    /// <summary>The value has characters outside ASCII, which HTTP headers can't carry here.</summary>
    NonAsciiValue,
}
