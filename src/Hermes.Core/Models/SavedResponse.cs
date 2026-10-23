namespace Hermes.Core.Models;

/// <summary>A response kept with its request so earlier results can be compared after reopening.</summary>
public sealed class SavedResponse
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public ResponseModel Response { get; set; } = new();

    /// <summary>The request as it went on the wire.</summary>
    public string SentRequest { get; set; } = "";

    /// <summary>True when the body was too large to keep; status, headers and timing are still saved.</summary>
    public bool BodyOmitted { get; set; }
}
