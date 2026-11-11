namespace Hermes.Core.Auth;

/// <summary>Where the SSL verification setting used for a token request comes from, so an error can say where to change it.</summary>
public enum SslSettingSource
{
    /// <summary>The app-wide setting: edited on a folder or collection, or on a request with no setting of its own.</summary>
    AppWide,

    /// <summary>The request's own override.</summary>
    Request,
}
