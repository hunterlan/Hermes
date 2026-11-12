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
    HttpClientProvider http,
    ManualService manual)
{
    public WorkspaceService Workspace { get; } = workspace;
    public RequestExecutor Executor { get; } = executor;
    public OAuth2Service OAuth { get; } = oauth;
    public DialogService Dialogs { get; } = dialogs;
    public SettingsService Settings { get; } = settings;
    public HttpClientProvider Http { get; } = http;
    public ManualService Manual { get; } = manual;

    /// <summary>
    /// Opens (or focuses) the tab of the folder or collection with the given id. Set by the main window, which owns
    /// the tabs, so a tab can link to another without knowing about it; does nothing until then.
    /// </summary>
    public Func<Guid, Task>? OpenContainerAsync { get; set; }

    /// <summary>Opens the app's Settings dialog. Set by the main window, like <see cref="OpenContainerAsync"/>.</summary>
    public Func<Task>? OpenSettingsAsync { get; set; }

    public AuthEditorViewModel CreateAuthEditor(bool allowInherit) =>
        new(OAuth, Dialogs.OpenUrlAsync, Dialogs.CopyTextAsync, allowInherit)
        {
            VerifySslProvider = () => Settings.Current.VerifySsl,
        };
}
