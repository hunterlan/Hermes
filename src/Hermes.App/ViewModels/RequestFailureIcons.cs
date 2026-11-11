using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Which icon a failure shows, shared by the response pane and the Auth panel.</summary>
public static class RequestFailureIcons
{
    /// <summary>The resource key of the icon for a failure of the given kind.</summary>
    public static string KeyFor(RequestFailureKind? kind) => kind switch
    {
        RequestFailureKind.SecureConnectionFailed or RequestFailureKind.PlainHttpAnswered or RequestFailureKind.CertificateExpired or RequestFailureKind.CertificateSelfSigned
            or RequestFailureKind.CertificateUntrustedAuthority or RequestFailureKind.CertificateNameMismatch => "IconLock",
        RequestFailureKind.BodyFileUnreadable => "IconFile",
        _ => "IconAlert",
    };
}
