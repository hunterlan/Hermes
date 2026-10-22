namespace Hermes.Core.Models;

/// <summary>Per-request overrides. <c>null</c> means "use the app-wide default".</summary>
public sealed class RequestSettings
{
    public bool? FollowRedirects { get; set; }
    public bool? VerifySsl { get; set; }
    public int? TimeoutMs { get; set; }
}
