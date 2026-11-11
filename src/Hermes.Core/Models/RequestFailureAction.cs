namespace Hermes.Core.Models;

/// <summary>A next step the user can take from a failure. The view gives each its label and behaviour.</summary>
public enum RequestFailureAction
{
    /// <summary>Sets the request's own SSL verification override to off and sends again.</summary>
    TurnOffSslVerification,

    /// <summary>Shows the request's Settings section, expanded.</summary>
    OpenRequestSettings,

    /// <summary>Puts the cursor in the request's URL.</summary>
    EditUrl,

    /// <summary>Sends the request again.</summary>
    Retry,

    /// <summary>Lets the user pick another file for the body, in place of <see cref="RequestFailure.Subject"/>.</summary>
    ChooseFile,

    /// <summary>Shows the request's Headers section.</summary>
    ShowHeaders,

    /// <summary>Opens the app's Settings dialog, where the app-wide SSL verification is.</summary>
    OpenAppSettings,

    /// <summary>Puts the cursor in the OAuth 2.0 Callback URL field.</summary>
    EditCallbackUrl,
}
