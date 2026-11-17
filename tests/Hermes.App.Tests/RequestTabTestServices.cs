using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>Builds the services a request tab needs on top of a temp workspace, without opening a window.</summary>
internal static class RequestTabTestServices
{
    public static TabServices Create(TempWorkspace temp) =>
        new(temp.Workspace, new RequestSendService(temp.Workspace, App.Services.GetRequiredService<IRequestExecutor>()),
            App.Services.GetRequiredService<OAuth2Service>(), new DialogService(), temp.Settings,
            App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
}
