using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public abstract partial class TabViewModelBase(TabServices services) : ObservableObject
{
    protected TabServices Services { get; } = services;

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }

    /// <summary>Opened by a single click: shown in italics and replaced by the next single click, like a browser preview tab.</summary>
    [ObservableProperty] public partial bool IsPreview { get; set; }

    /// <summary>Pinned tabs show a pin, have no close button and survive "Close other/all tabs".</summary>
    [ObservableProperty] public partial bool IsPinned { get; set; }

    /// <summary>Collection name shown before the title when another open tab has the same title.</summary>
    [ObservableProperty] public partial string? TitlePrefix { get; set; }

    /// <summary>Name of the collection the tab's entity lives in, used to tell same-named tabs apart.</summary>
    public virtual string? CollectionName => null;

    // Editing a preview tab keeps it.
    partial void OnIsDirtyChanged(bool value)
    {
        if (value)
            IsPreview = false;
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (value)
            IsPreview = false;
    }

    public abstract SessionTabKind Kind { get; }

    /// <summary>The persisted entity this tab edits; used to focus an existing tab instead of opening a duplicate.</summary>
    public abstract Guid EntityId { get; }

    /// <summary>HTTP method badge for request tabs; null for other tabs.</summary>
    public virtual string? Method => null;

    /// <summary>Short label shown instead of a method for non-request tabs (e.g. "ENV").</summary>
    public virtual string? KindLabel => null;

    /// <summary>Saves the tab. Returns false when the user cancelled (e.g. closed the "Save as" dialog).</summary>
    public abstract Task<bool> SaveAsync();

    public virtual SessionTab ToSession() => new() { Kind = Kind, EntityId = EntityId, IsPinned = IsPinned };

    /// <summary>Called when the active environment or any variables changed.</summary>
    public virtual void OnVariablesChanged() { }

    /// <summary>Called when collections changed (renames, moves, deletes).</summary>
    public virtual void OnWorkspaceChanged() { }
}
