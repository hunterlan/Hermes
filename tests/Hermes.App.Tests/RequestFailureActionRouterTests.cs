using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>Each next step picked on a failure reaches the part of the request that handles it.</summary>
public class RequestFailureActionRouterTests
{
    /// <summary>The parts a router works on, with a send and an "open app settings" that only count their runs.</summary>
    private sealed class RoutedRequest : IDisposable
    {
        private readonly Window _window = new();
        private readonly OAuth2Service _oauth = new();

        public RoutedRequest()
        {
            _window.Show();
            var dialogs = new DialogService { Owner = _window };
            UrlEditor = new RequestUrlEditorViewModel();
            Body = new RequestBodyViewModel(dialogs);
            Settings = new RequestSettingsViewModel(App.Services.GetRequiredService<SettingsService>());
            Sections = new ComposerSectionsViewModel(UrlEditor, Body, Settings);
            Auth = new AuthEditorViewModel(_oauth, _ => Task.FromResult(true), _ => Task.CompletedTask, allowInherit: true);
            var send = new AsyncRelayCommand(CountSend);
            var openAppSettings = new RelayCommand(() => AppSettingsOpenings++);
            Router = new RequestFailureActionRouter(UrlEditor, Body, Settings, Sections, Auth, dialogs, send, openAppSettings);
        }

        public RequestUrlEditorViewModel UrlEditor { get; }
        public RequestBodyViewModel Body { get; }
        public RequestSettingsViewModel Settings { get; }
        public ComposerSectionsViewModel Sections { get; }
        public AuthEditorViewModel Auth { get; }
        public RequestFailureActionRouter Router { get; }
        public int Sends { get; private set; }
        public int AppSettingsOpenings { get; private set; }

        /// <summary>The verification override as it was when the request was sent.</summary>
        public bool? VerifySslOverrideWhenSent { get; private set; }

        public void Pick(RequestFailureAction action, string? subject = null) =>
            Router.OnFailureActionRequested(this, new RequestFailureActionEventArgs(action,
                RequestFailure.FromMessage("A failure to act on.") with { Subject = subject }));

        private Task CountSend()
        {
            Sends++;
            VerifySslOverrideWhenSent = Settings.VerifySslOverride;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Auth.Dispose();
            _oauth.Dispose();
            _window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Turning_off_verification_sets_the_request_override_and_sends_again()
    {
        using var request = new RoutedRequest();

        request.Pick(RequestFailureAction.TurnOffSslVerification);
        await request.Router.TurnOffVerificationAndResendCommand.ExecutionTask!;

        Assert.False(request.Settings.VerifySslOverride);
        Assert.Equal(1, request.Sends);
        Assert.False(request.VerifySslOverrideWhenSent);
    }

    [AvaloniaFact]
    public void Retry_sends_again_without_changing_the_settings()
    {
        using var request = new RoutedRequest();

        request.Pick(RequestFailureAction.Retry);

        Assert.Equal(1, request.Sends);
        Assert.Null(request.Settings.VerifySslOverride);
    }

    [AvaloniaFact]
    public void Open_request_settings_reveals_the_settings_section()
    {
        using var request = new RoutedRequest();
        var revealRequests = 0;
        request.Sections.SettingsRevealRequested += () => revealRequests++;

        request.Pick(RequestFailureAction.OpenRequestSettings);

        Assert.True(request.Sections.IsSettingsSectionVisible);
        Assert.True(request.Sections.SettingsExpanded);
        Assert.Equal(1, revealRequests);
        Assert.Equal(0, request.Sends);
    }

    [AvaloniaFact]
    public void Show_headers_reveals_the_headers_section()
    {
        using var request = new RoutedRequest();
        request.Sections.HeadersExpanded = false;
        var revealRequests = 0;
        request.Sections.HeadersRevealRequested += () => revealRequests++;

        request.Pick(RequestFailureAction.ShowHeaders);

        Assert.True(request.Sections.HeadersExpanded);
        Assert.Equal(1, revealRequests);
    }

    [AvaloniaFact]
    public void Edit_url_asks_for_the_cursor_in_the_url()
    {
        using var request = new RoutedRequest();
        var focusRequests = 0;
        request.UrlEditor.FocusRequested += () => focusRequests++;

        request.Pick(RequestFailureAction.EditUrl);

        Assert.Equal(1, focusRequests);
    }

    [AvaloniaFact]
    public void Edit_callback_url_asks_for_the_cursor_in_the_auth_callback_url()
    {
        using var request = new RoutedRequest();
        var focused = new List<AuthEditorField>();
        request.Auth.FocusRequested += focused.Add;

        request.Pick(RequestFailureAction.EditCallbackUrl);

        Assert.Equal([AuthEditorField.CallbackUrl], focused);
    }

    [AvaloniaFact]
    public void Open_app_settings_opens_the_app_settings()
    {
        using var request = new RoutedRequest();

        request.Pick(RequestFailureAction.OpenAppSettings);

        Assert.Equal(1, request.AppSettingsOpenings);
        Assert.Equal(0, request.Sends);
    }

    [AvaloniaFact]
    public async Task Choose_file_asks_for_a_file_and_keeps_the_body_when_none_is_picked()
    {
        using var request = new RoutedRequest();
        request.Body.SelectedBodyMode = RequestBodyViewModel.BodyModes.Single(mode => mode.Value == BodyMode.Binary);
        request.Body.BinaryFilePath = "/old/payload.bin";

        request.Pick(RequestFailureAction.ChooseFile, subject: "/old/payload.bin");
        // The headless file picker returns no file, as cancelling the picker does.
        await request.Router.ChooseBodyFileCommand.ExecutionTask!;

        Assert.Equal("/old/payload.bin", request.Body.BinaryFilePath);
        Assert.Equal(0, request.Sends);
    }

    [AvaloniaFact]
    public async Task Every_failure_action_has_a_handler()
    {
        using var request = new RoutedRequest();

        foreach (var action in Enum.GetValues<RequestFailureAction>())
        {
            var exception = Record.Exception(() => request.Pick(action));
            Assert.True(exception is null, $"{action} has no handler: {exception?.Message}");
        }

        // Turning verification off and choosing a file start work; let it finish before the window goes.
        await request.Router.TurnOffVerificationAndResendCommand.ExecutionTask!;
        await request.Router.ChooseBodyFileCommand.ExecutionTask!;
    }
}
