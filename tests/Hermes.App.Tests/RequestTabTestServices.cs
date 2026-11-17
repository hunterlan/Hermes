using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>Builds the services a request tab needs on top of a temp workspace, without opening a window.</summary>
internal static class RequestTabTestServices
{
    public static TabServices Create(TempWorkspace temp)
    {
        var dialogs = new DialogService();
        return new TabServices(temp.Workspace,
            new RequestSendService(temp.Workspace, App.Services.GetRequiredService<IRequestExecutor>()),
            new RequestSaveService(temp.Workspace, dialogs),
            App.Services.GetRequiredService<OAuth2Service>(), dialogs, temp.Settings,
            App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
    }
}
