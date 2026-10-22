using Hermes.App.Services;
using Hermes.Core.Auth;
using Hermes.Core.Http;

namespace Hermes.App.ViewModels;

/// <summary>Dependencies shared by all tabs.</summary>
public sealed class TabServices(
    WorkspaceService workspace,
    RequestExecutor executor,
    OAuth2Service oauth,
    DialogService dialogs,
    SettingsService settings,
    HttpClientProvider http)
{
    public WorkspaceService Workspace { get; } = workspace;
    public RequestExecutor Executor { get; } = executor;
    public OAuth2Service OAuth { get; } = oauth;
    public DialogService Dialogs { get; } = dialogs;
    public SettingsService Settings { get; } = settings;
    public HttpClientProvider Http { get; } = http;

    public AuthEditorViewModel CreateAuthEditor(bool allowInherit) =>
        new(OAuth, Dialogs.OpenUrlAsync, allowInherit);
}
