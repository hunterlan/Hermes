using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>How a tab lays out its response pane: stacked or side by side, folded to a bar, and the split it keeps.</summary>
public sealed partial class ResponsePaneLayoutViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;

    public ResponsePaneLayoutViewModel(SettingsService settings)
    {
        _settings = settings;
        // The layout can change from any tab or from Settings; this tab's layout buttons and collapse follow it.
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// The pane is folded to a bar, giving the request editor the tab. Kept in side-by-side layout, which shows the
    /// pane in full for now, so the tab is collapsed again after switching back to stacked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCollapsedBar))]
    public partial bool IsResponseCollapsed { get; set; }

    /// <summary>The pane's share of the stacked split while expanded, between 0 and 1. Collapsing keeps it.</summary>
    [ObservableProperty] public partial double ResponseSplitRatio { get; set; } = SessionTab.DefaultResponseSplitRatio;

    public bool IsSideBySide => _settings.Current.Layout == ResponseLayout.Horizontal;

    /// <summary>The bar shows instead of the full pane.</summary>
    public bool ShowsCollapsedBar => IsResponseCollapsed && CanToggleCollapsed;

    /// <summary>Only stacked layout collapses the pane for now.</summary>
    public bool CanToggleCollapsed => !IsSideBySide;

    [RelayCommand(CanExecute = nameof(CanToggleCollapsed))]
    private void ToggleCollapsed() => IsResponseCollapsed = !IsResponseCollapsed;

    [RelayCommand]
    private void SetLayout(ResponseLayout layout)
    {
        _settings.Current.Layout = layout;
        // Raises Changed, which updates IsSideBySide here and in the other tabs.
        _settings.SaveCurrent();
    }

    /// <summary>Opens the pane, for when a result arrives: a result is never hidden.</summary>
    public void Expand() => IsResponseCollapsed = false;

    /// <summary>Puts the pane back as the session kept it. A ratio that can't be a split falls back to the default.</summary>
    public void RestorePane(bool isCollapsed, double splitRatio)
    {
        IsResponseCollapsed = isCollapsed;
        ResponseSplitRatio = splitRatio is > 0 and < 1 ? splitRatio : SessionTab.DefaultResponseSplitRatio;
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(IsSideBySide));
        OnPropertyChanged(nameof(CanToggleCollapsed));
        OnPropertyChanged(nameof(ShowsCollapsedBar));
        ToggleCollapsedCommand.NotifyCanExecuteChanged();
    }
}
