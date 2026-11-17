using Hermes.Core.Models;
using Hermes.Core.Requests;

namespace Hermes.App.ViewModels;

/// <summary>The request a tab edits, as it is at the moment a part of the tab asks for it.</summary>
public interface IRequestSource
{
    /// <summary>The request's id. Saving it as a new request (Save As) gives it a new one.</summary>
    Guid RequestId { get; }

    /// <summary>Whether the request is saved in the workspace, so a response can be kept with it.</summary>
    bool IsPersisted { get; }

    /// <summary>The request as it is in the editor now.</summary>
    HttpRequestModel BuildModel();

    /// <summary>What the request is resolved with where it is now: variables, environment and inherited settings.</summary>
    RequestContext CreateContext();
}
