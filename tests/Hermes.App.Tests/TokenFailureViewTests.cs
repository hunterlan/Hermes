using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Auth;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The Auth panel's failure block: what it shows when getting a token fails, and what its one action does.</summary>
public class TokenFailureViewTests
{
    private static AuthEditorViewModel CreateEditor(OAuth2Service oauth, OAuth2Auth settings, Func<string, Task>? copyText = null,
        Func<Uri, Task<bool>>? openBrowser = null)
    {
        var editor = new AuthEditorViewModel(oauth, openBrowser ?? (_ => Task.FromResult(true)), copyText ?? (_ => Task.CompletedTask), allowInherit: false);
        editor.Load(new AuthConfig { Type = AuthType.OAuth2, OAuth2 = settings });
        return editor;
    }

    private static OAuth2Auth ClientCredentials(string tokenUrl) =>
        new() { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = tokenUrl, ClientId = "client" };

    private static (Window Window, AuthEditorView View) Show(AuthEditorViewModel editor)
    {
        var view = new AuthEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        UiTestHelpers.Pump();
        return (window, view);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    [AvaloniaFact]
    public async Task A_refused_connection_shows_headline_facts_and_a_retry_with_details_collapsed()
    {
        using var oauth = new OAuth2Service();
        var copied = "";
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"), text => Task.FromResult(copied = text));
        var (window, view) = Show(editor);

        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal("", editor.TokenStatus);
        Assert.Equal("Server isn't accepting connections", Find<SelectableTextBlock>(view, "TokenFailureHeadlineText").Text);
        Assert.StartsWith("Nothing is listening on", Find<SelectableTextBlock>(view, "TokenFailureFactsText").Text);
        Assert.Equal("Try again", Find<Button>(view, "TokenFailureActionButton").Content);
        Assert.False(Find<CollapsibleSection>(view, "TokenFailureDetailsSection").IsExpanded);

        editor.Failure!.IsDetailsExpanded = true;
        UiTestHelpers.Pump();
        Find<Button>(view, "CopyTokenFailureDetailsButton").Command!.Execute(null);
        Assert.StartsWith("Server isn't accepting connections", copied);
        Assert.Contains("SocketException", copied);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Try_again_fetches_a_token_again()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        var first = editor.Failure;

        await editor.RunFailureActionCommand.ExecuteAsync(null);

        Assert.NotNull(editor.Failure);
        Assert.NotSame(first, editor.Failure);
    }

    [AvaloniaFact]
    public async Task A_certificate_failure_for_the_app_wide_setting_opens_the_app_settings()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials(server.Url + "token"));
        var openedSettings = 0;
        editor.FailureActionRequested += (_, args) => openedSettings += args.Action == RequestFailureAction.OpenAppSettings ? 1 : 0;
        var (window, view) = Show(editor);

        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal("The app-wide SSL setting applies to token requests.", Find<TextBlock>(view, "TokenFailureActionCaptionText").Text);
        Assert.Equal("Open settings", Find<Button>(view, "TokenFailureActionButton").Content);

        Find<Button>(view, "TokenFailureActionButton").Command!.Execute(null);
        Assert.Equal(1, openedSettings);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_certificate_failure_for_a_request_override_opens_the_request_settings()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials(server.Url + "token"));
        var openedRequestSettings = 0;
        editor.SslSourceProvider = () => SslSettingSource.Request;
        editor.FailureActionRequested += (_, args) => openedRequestSettings += args.Action == RequestFailureAction.OpenRequestSettings ? 1 : 0;
        var (window, view) = Show(editor);

        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal("Token requests follow this request's SSL setting.", Find<TextBlock>(view, "TokenFailureActionCaptionText").Text);
        Assert.Equal("Open request settings", Find<Button>(view, "TokenFailureActionButton").Content);

        Find<Button>(view, "TokenFailureActionButton").Command!.Execute(null);
        Assert.Equal(1, openedRequestSettings);
        window.Close();
    }

    [AvaloniaFact]
    public void A_request_tab_says_which_setting_applies_by_whether_it_has_its_own_and_can_open_both()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            Assert.Equal(SslSettingSource.AppWide, tab.Auth.SslSourceProvider());

            tab.VerifySslOverride = false;
            Assert.Equal(SslSettingSource.Request, tab.Auth.SslSourceProvider());

            tab.UseDefaultSettingsCommand.Execute(null);
            Assert.Equal(SslSettingSource.AppWide, tab.Auth.SslSourceProvider());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_request_tab_handles_the_next_steps_of_its_auth_failure()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        // Set after the window: the main window installs the real dialog when it is created.
        var services = App.Services.GetRequiredService<TabServices>();
        var appSettingsOpened = 0;
        var originalOpenSettings = services.OpenSettingsAsync;
        services.OpenSettingsAsync = () =>
        {
            appSettingsOpened++;
            return Task.CompletedTask;
        };
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            Assert.False(tab.IsSettingsSectionVisible);

            ShowAuthFailure(tab.Auth, RequestFailureAction.OpenRequestSettings);
            tab.Auth.RunFailureActionCommand.Execute(null);
            Assert.True(tab.SettingsExpanded);

            ShowAuthFailure(tab.Auth, RequestFailureAction.OpenAppSettings);
            tab.Auth.RunFailureActionCommand.Execute(null);
            Assert.Equal(1, appSettingsOpened);
        }
        finally
        {
            services.OpenSettingsAsync = originalOpenSettings;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_collection_tab_opens_the_app_settings_for_its_auth_failure()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var services = App.Services.GetRequiredService<TabServices>();
        var appSettingsOpened = 0;
        var originalOpenSettings = services.OpenSettingsAsync;
        // Set after the window: the main window installs the real dialog when it is created.
        services.OpenSettingsAsync = () =>
        {
            appSettingsOpened++;
            return Task.CompletedTask;
        };
        var workspace = App.Services.GetRequiredService<WorkspaceService>();
        var collection = workspace.CreateCollection("Auth failure " + Guid.NewGuid());
        try
        {
            await vm.OpenCollectionAsync(collection);
            var tab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);

            ShowAuthFailure(tab.Auth, RequestFailureAction.OpenAppSettings);
            tab.Auth.RunFailureActionCommand.Execute(null);

            Assert.Equal(1, appSettingsOpened);
        }
        finally
        {
            services.OpenSettingsAsync = originalOpenSettings;
            workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    private static void ShowAuthFailure(AuthEditorViewModel auth, RequestFailureAction action) =>
        auth.Failure = new RequestFailureViewModel(
            new RequestFailure { Kind = RequestFailureKind.Other, Headline = "Failed", Facts = "A failure.", Actions = [action] },
            _ => Task.CompletedTask);

    [AvaloniaFact]
    public async Task A_taken_callback_port_asks_the_view_to_focus_the_callback_url()
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;
            using var oauth = new OAuth2Service();
            var editor = CreateEditor(oauth, new OAuth2Auth
            {
                GrantType = OAuth2GrantType.AuthorizationCode,
                AuthUrl = "https://auth.test/authorize",
                AccessTokenUrl = "https://auth.test/token",
                ClientId = "client",
                RedirectUri = $"http://localhost:{port}/callback",
            });
            var focused = new List<AuthEditorField>();
            editor.FocusRequested += focused.Add;
            var (window, view) = Show(editor);

            await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
            UiTestHelpers.Pump();

            Assert.Equal($"Port {port} is already in use", Find<SelectableTextBlock>(view, "TokenFailureHeadlineText").Text);
            Assert.Equal("Choose another callback port.", Find<SelectableTextBlock>(view, "TokenFailureFactsText").Text);
            Assert.Equal([AuthEditorField.CallbackUrl], focused);
            window.Close();
        }
        finally
        {
            taken.Stop();
        }
    }

    [AvaloniaFact]
    public async Task An_unexpected_exception_shows_a_generic_headline_with_the_message_under_details()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, new OAuth2Auth
        {
            GrantType = OAuth2GrantType.AuthorizationCode,
            AuthUrl = "https://auth.test/authorize",
            AccessTokenUrl = "https://auth.test/token",
            ClientId = "client",
            RedirectUri = $"http://localhost:{ClosedPort.Find()}/callback",
        }, openBrowser: _ => throw new InvalidOperationException("Object reference not set to an instance of an object."));
        var (window, view) = Show(editor);

        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal("Couldn't get a token", Find<SelectableTextBlock>(view, "TokenFailureHeadlineText").Text);
        var facts = Find<SelectableTextBlock>(view, "TokenFailureFactsText").Text;
        Assert.EndsWith("Copy the details if you report this.", facts);
        Assert.DoesNotContain("Object reference", facts);
        editor.Failure!.IsDetailsExpanded = true;
        UiTestHelpers.Pump();
        Assert.Contains("Object reference not set", Find<SelectableTextBlock>(view, "TokenFailureOriginalMessageText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_new_attempt_clears_the_previous_failure()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Failure);

        editor.ClearTokenCommand.Execute(null);

        Assert.Null(editor.Failure);
    }

    [AvaloniaFact]
    public async Task A_failure_appearing_expanding_and_being_replaced_does_not_mark_the_auth_settings_changed()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        var changes = 0;
        editor.Changed += () => changes++;

        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Failure);
        editor.Failure.IsDetailsExpanded = true;
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);

        Assert.NotNull(editor.Failure);
        Assert.False(editor.Failure.IsDetailsExpanded);
        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public async Task Changing_the_grant_type_clears_the_failure()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Failure);

        editor.SelectedGrantType = editor.GrantTypes.First(grant => grant.Value == OAuth2GrantType.Password);

        Assert.Null(editor.Failure);
    }

    [AvaloniaFact]
    public async Task Changing_the_auth_type_away_and_back_clears_the_failure()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        Assert.NotNull(editor.Failure);

        editor.SelectedType = editor.Types.First(type => type.Value == AuthType.Bearer);
        editor.SelectedType = editor.Types.First(type => type.Value == AuthType.OAuth2);

        Assert.Null(editor.Failure);
    }

    [AvaloniaFact]
    public async Task Editing_the_token_url_keeps_the_failure_on_screen()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        var failure = editor.Failure;
        Assert.NotNull(failure);

        editor.OAuthTokenUrl = "http://127.0.0.1:1/other";
        editor.OAuthClientId = "another";

        Assert.Same(failure, editor.Failure);
    }

    [AvaloniaFact]
    public async Task Clearing_the_failure_on_a_grant_type_change_marks_the_settings_changed_only_once()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));
        await editor.GetNewAccessTokenCommand.ExecuteAsync(null);
        var changes = 0;
        editor.Changed += () => changes++;

        editor.SelectedGrantType = editor.GrantTypes.First(grant => grant.Value == OAuth2GrantType.Password);

        Assert.Null(editor.Failure);
        Assert.Equal(1, changes);
    }
}
