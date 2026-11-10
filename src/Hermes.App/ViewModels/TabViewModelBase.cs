using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public abstract partial class TabViewModelBase(TabServices services) : ObservableObject, IDisposable
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

    /// <summary>Shows the app's Settings dialog.</summary>
    [RelayCommand]
    private Task OpenAppSettings() => Services.OpenSettingsAsync?.Invoke() ?? Task.CompletedTask;

    /// <summary>
    /// The next steps of an Auth panel's failure that a tab without request settings can take. A tab with its own
    /// handler for them, like a request, doesn't use this.
    /// </summary>
    protected void OnAuthFailureActionRequested(object? sender, RequestFailureActionEventArgs args)
    {
        switch (args.Action)
        {
            case RequestFailureAction.OpenAppSettings:
                OpenAppSettingsCommand.Execute(null);
                break;
            default:
                throw new UnreachableException($"Unhandled failure action {args.Action}");
        }
    }

    partial void OnIsActiveChanged(bool value) => OnActiveChanged(value);

    /// <summary>Called when the tab is shown or left. A tab uses it to mask what the user revealed.</summary>
    protected virtual void OnActiveChanged(bool isActive) { }

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

    /// <summary>Resource key of the icon shown instead of a method for non-request tabs (e.g. "IconEnvironment").</summary>
    public virtual string? KindIconKey => null;

    /// <summary>Accessible name of <see cref="KindIconKey"/> (e.g. "Environment").</summary>
    public virtual string? KindName => null;

    /// <summary>Saves the tab. Returns false when the user cancelled (e.g. closed the "Save as" dialog).</summary>
    public abstract Task<bool> SaveAsync();

    /// <summary>Unsaved edits survive a restart in the session; otherwise quitting asks to save them.</summary>
    public virtual bool KeepsDraftInSession => false;

    public virtual SessionTab ToSession() => new() { Kind = Kind, EntityId = EntityId, IsPinned = IsPinned };

    /// <summary>
    /// Called once the tab is closed or replaced. A tab cancels what it still has running (a send, a token request)
    /// and frees what it holds (a preview image).
    /// </summary>
    public virtual void Dispose() { }

    /// <summary>Called when the app settings changed.</summary>
    public virtual void OnSettingsChanged() { }

    /// <summary>Called when the active environment or any variables changed.</summary>
    public virtual void OnVariablesChanged() { }

    /// <summary>Called when collections changed (renames, moves, deletes).</summary>
    public virtual void OnWorkspaceChanged() { }
}
