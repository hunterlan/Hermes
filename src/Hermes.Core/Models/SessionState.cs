namespace Hermes.Core.Models;

public enum SessionTabKind
{
    Request,
    Collection,
    Folder,
    Environment,
    EnvironmentMatrix,
}

public sealed class SessionTab
{
    public SessionTabKind Kind { get; set; }

    /// <summary>The persisted entity the tab is bound to (request, folder, collection or environment id).</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Unsaved state of a request tab.</summary>
    public HttpRequestModel? RequestDraft { get; set; }

    public bool IsPinned { get; set; }
}

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public Guid? ActiveEnvironmentId { get; set; }
    public double SidebarWidth { get; set; } = 300;
}
