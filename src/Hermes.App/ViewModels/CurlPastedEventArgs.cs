using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>A cURL command was pasted into the URL bar: the request it describes, to load in its place.</summary>
public sealed class CurlPastedEventArgs(HttpRequestModel request) : EventArgs
{
    public HttpRequestModel Request { get; } = request;
}
