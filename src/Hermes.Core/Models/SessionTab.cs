namespace Hermes.Core.Models;

public sealed class SessionTab
{
    /// <summary>The response pane's share of a request tab's split when nothing else was saved.</summary>
    public const double DefaultResponseSplitRatio = 0.5;

    public SessionTabKind Kind { get; set; }

    /// <summary>The persisted entity the tab is bound to (request, folder, collection or environment id).</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Unsaved state of a request tab.</summary>
    public HttpRequestModel? RequestDraft { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>A request tab's response pane is collapsed to its bar (stacked) or strip (side by side).</summary>
    public bool IsResponseCollapsed { get; set; }

    /// <summary>The response pane's share of a request tab's height in stacked layout while expanded, between 0 and 1.</summary>
    public double ResponseSplitRatio { get; set; } = DefaultResponseSplitRatio;

    /// <summary>The response pane's share of a request tab's width in side-by-side layout while expanded, between 0 and 1.</summary>
    public double SideBySideResponseSplitRatio { get; set; } = DefaultResponseSplitRatio;
}
