using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>
/// Takes each next step the person picks on a request's failure, in the response or the Auth panel, to the part of the
/// request tab that handles it: the composer sections, the request settings, the URL bar, the body, the auth editor,
/// a resend or the app's settings.
/// </summary>
/// <param name="send">Sends the request again, for Retry and after turning off verification.</param>
/// <param name="openAppSettings">Shows the app's Settings dialog.</param>
public sealed partial class RequestFailureActionRouter(
    RequestUrlEditorViewModel urlEditor,
    RequestBodyViewModel body,
    RequestSettingsViewModel settings,
    ComposerSectionsViewModel sections,
    AuthEditorViewModel auth,
    DialogService dialogs,
    IAsyncRelayCommand send,
    ICommand openAppSettings)
{
    /// <summary>Handles the action picked on a failure shown by the response or the Auth panel.</summary>
    public void OnFailureActionRequested(object? sender, RequestFailureActionEventArgs args)
    {
        switch (args.Action)
        {
            case RequestFailureAction.TurnOffSslVerification:
                TurnOffVerificationAndResendCommand.Execute(null);
                break;
            case RequestFailureAction.OpenRequestSettings:
                sections.RevealSettings();
                break;
            case RequestFailureAction.EditUrl:
                urlEditor.RequestFocus();
                break;
            case RequestFailureAction.Retry:
                send.Execute(null);
                break;
            case RequestFailureAction.ChooseFile:
                ChooseBodyFileCommand.Execute(args.Failure.Subject);
                break;
            case RequestFailureAction.ShowHeaders:
                sections.RevealHeaders();
                break;
            case RequestFailureAction.OpenAppSettings:
                openAppSettings.Execute(null);
                break;
            case RequestFailureAction.EditCallbackUrl:
                auth.RequestFocus(AuthEditorField.CallbackUrl);
                break;
            default:
                throw new UnreachableException($"Unhandled failure action {args.Action}");
        }
    }

    /// <summary>"Turn off verification for this request": the request's own override, not the app-wide setting.</summary>
    [RelayCommand]
    private async Task TurnOffVerificationAndResendAsync()
    {
        settings.VerifySslOverride = false;
        await send.ExecuteAsync(null);
    }

    /// <summary>
    /// "Choose file" after the body's file couldn't be read: asks for another file and uses it. When no field holds
    /// the unreadable path (it came from a variable) there is nothing to replace, so no file is asked for and the
    /// Body section is shown instead, where the user decides.
    /// </summary>
    [RelayCommand]
    private async Task ChooseBodyFileAsync(string? unreadablePath)
    {
        if (!body.HoldsFilePath(unreadablePath))
        {
            sections.RevealBody();
            return;
        }

        if (await dialogs.PickFileAsync("Select file") is { } picked)
            UseChosenBodyFile(picked, unreadablePath);
    }

    /// <summary>
    /// Puts <paramref name="picked"/> in place of the file that couldn't be read. When the body has no field holding
    /// that path (it came from a variable), nothing is overwritten and the Body section is shown, so the user decides.
    /// </summary>
    /// <returns>True when a field was replaced.</returns>
    public bool UseChosenBodyFile(string picked, string? unreadablePath)
    {
        var replaced = body.UseChosenBodyFile(picked, unreadablePath);
        if (!replaced)
            sections.RevealBody();
        return replaced;
    }
}
