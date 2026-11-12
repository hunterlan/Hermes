namespace Hermes.Core.Http;

/// <summary>A header of the request can't be sent as it is written.</summary>
public sealed class InvalidHeaderException(string headerName, string headerValue, InvalidHeaderProblem problem)
    : Exception($"The header '{headerName}' can't be sent.")
{
    public string HeaderName { get; } = headerName;
    public string HeaderValue { get; } = headerValue;
    public InvalidHeaderProblem Problem { get; } = problem;
}
