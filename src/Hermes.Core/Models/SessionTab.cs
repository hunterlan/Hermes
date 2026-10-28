namespace Hermes.Core.Models;

public sealed class SessionTab
{
    public SessionTabKind Kind { get; set; }

    /// <summary>The persisted entity the tab is bound to (request, folder, collection or environment id).</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Unsaved state of a request tab.</summary>
    public HttpRequestModel? RequestDraft { get; set; }

    public bool IsPinned { get; set; }
}
