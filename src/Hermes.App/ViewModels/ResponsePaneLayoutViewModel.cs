using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>
/// How a tab lays out its response pane: stacked or side by side, folded to a bar (stacked) or a strip (side by
/// side), and the split each layout keeps.
/// </summary>
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
    /// The pane is folded, giving the request editor the tab. Shared by both layouts: switching layout keeps the tab
    /// collapsed, as a bar when stacked and as a strip when side by side.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCollapsedPane))]
    [NotifyPropertyChangedFor(nameof(ShowsCollapsedBar))]
    [NotifyPropertyChangedFor(nameof(ShowsCollapsedStrip))]
    public partial bool IsResponseCollapsed { get; set; }

    /// <summary>The pane's share of the stacked split while expanded, between 0 and 1. Collapsing keeps it.</summary>
    [ObservableProperty] public partial double ResponseSplitRatio { get; set; } = SessionTab.DefaultResponseSplitRatio;

    /// <summary>The pane's share of the side-by-side split (its width) while expanded, between 0 and 1. Collapsing keeps it.</summary>
    [ObservableProperty] public partial double SideBySideResponseSplitRatio { get; set; } = SessionTab.DefaultResponseSplitRatio;

    public bool IsSideBySide => _settings.Current.Layout == ResponseLayout.Horizontal;

    /// <summary>The pane is folded, in either layout: the full pane is hidden.</summary>
    public bool ShowsCollapsedPane => IsResponseCollapsed;

    /// <summary>The bar at the bottom of the tab shows instead of the full pane (stacked layout).</summary>
    public bool ShowsCollapsedBar => IsResponseCollapsed && !IsSideBySide;

    /// <summary>The vertical strip at the right edge of the tab shows instead of the full pane (side-by-side layout).</summary>
    public bool ShowsCollapsedStrip => IsResponseCollapsed && IsSideBySide;

    [RelayCommand]
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
    public void RestorePane(bool isCollapsed, double splitRatio, double sideBySideSplitRatio)
    {
        IsResponseCollapsed = isCollapsed;
        ResponseSplitRatio = ValidRatioOrDefault(splitRatio);
        SideBySideResponseSplitRatio = ValidRatioOrDefault(sideBySideSplitRatio);
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private static double ValidRatioOrDefault(double ratio) =>
        ratio is > 0 and < 1 ? ratio : SessionTab.DefaultResponseSplitRatio;

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(IsSideBySide));
        OnPropertyChanged(nameof(ShowsCollapsedBar));
        OnPropertyChanged(nameof(ShowsCollapsedStrip));
    }
}
