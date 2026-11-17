using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Views;

public partial class RequestTabView : UserControl
{
    private SettingsService? _settings;
    private RequestTabViewModel? _tab;

    public RequestTabView()
    {
        InitializeComponent();
        Splitter.DragCompleted += OnSplitterDragCompleted;
        Splitter.DoubleTapped += OnSplitterDoubleTapped;
    }

    private bool IsSideBySide => _settings?.Current.Layout == ResponseLayout.Horizontal;

    private double ResponseBarHeight => (double)this.FindResource("ResponseBarHeight")!;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _settings = App.Services?.GetService<SettingsService>();
        if (_settings is not null)
            _settings.Changed += ApplyLayout;
        ApplyLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_settings is not null)
            _settings.Changed -= ApplyLayout;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_tab is not null)
        {
            _tab.Sections.SettingsRevealRequested -= ScrollSettingsIntoView;
            _tab.Sections.HeadersRevealRequested -= ScrollHeadersIntoView;
            _tab.Sections.BodyRevealRequested -= ScrollBodyIntoView;
            _tab.UrlEditor.FocusRequested -= FocusUrl;
            _tab.Response.PropertyChanged -= OnResponsePropertyChanged;
        }
        _tab = DataContext as RequestTabViewModel;
        if (_tab is not null)
        {
            _tab.Sections.SettingsRevealRequested += ScrollSettingsIntoView;
            _tab.Sections.HeadersRevealRequested += ScrollHeadersIntoView;
            _tab.Sections.BodyRevealRequested += ScrollBodyIntoView;
            _tab.UrlEditor.FocusRequested += FocusUrl;
            _tab.Response.PropertyChanged += OnResponsePropertyChanged;
        }
        ApplyLayout();
    }

    private void OnResponsePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResponseViewModel.ShowsCollapsedBar) or nameof(ResponseViewModel.ResponseSplitRatio))
            ApplyLayout();
    }

    /// <summary>Keeps the split the user dragged to. Dragged down to the bar's height, the pane collapses instead.</summary>
    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (IsSideBySide || _tab?.Response is not { ShowsCollapsedBar: false } response)
            return;
        // The splitter sets the two lengths in proportion to the heights it dragged them to.
        var requestLength = SplitGrid.RowDefinitions[0].Height.Value;
        var responseLength = SplitGrid.RowDefinitions[2].Height.Value;
        var splitHeight = SplitGrid.RowDefinitions[0].ActualHeight + SplitGrid.RowDefinitions[2].ActualHeight;
        if (requestLength + responseLength <= 0 || splitHeight <= 0)
            return;
        var responseShare = responseLength / (requestLength + responseLength);
        if (responseShare * splitHeight <= ResponseBarHeight)
            // The share from before the drag stays, so expanding brings back the pane the user had.
            response.IsResponseCollapsed = true;
        else
            response.ResponseSplitRatio = responseShare;
    }

    private void OnSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tab?.Response.ToggleCollapsedCommand is not { } toggle || !toggle.CanExecute(null))
            return;
        toggle.Execute(null);
        e.Handled = true;
    }

    /// <summary>Waits for the section to be laid out (it may have just become visible), then scrolls the request editor to it.</summary>
    private void ScrollSettingsIntoView() =>
        Dispatcher.UIThread.Post(() => SettingsSection.BringIntoView(), DispatcherPriority.Loaded);

    private void ScrollHeadersIntoView() =>
        Dispatcher.UIThread.Post(() => HeadersSection.BringIntoView(), DispatcherPriority.Loaded);

    private void ScrollBodyIntoView() =>
        Dispatcher.UIThread.Post(() => BodySection.BringIntoView(), DispatcherPriority.Loaded);

    public void FocusUrl() => UrlBox.FocusEditor();

    /// <summary>
    /// Response below the request (vertical) or to its right (horizontal). Stacked, the rows come from the tab's
    /// response pane, its share of the split or the bar's height while collapsed, so rebuilding them on a settings
    /// change keeps both.
    /// </summary>
    private void ApplyLayout()
    {
        var horizontal = IsSideBySide;
        var collapsed = !horizontal && _tab?.Response.ShowsCollapsedBar == true;
        var responseShare = _tab?.Response.ResponseSplitRatio ?? SessionTab.DefaultResponseSplitRatio;
        SplitGrid.RowDefinitions.Clear();
        SplitGrid.ColumnDefinitions.Clear();
        if (horizontal)
        {
            SplitGrid.ColumnDefinitions.AddRange([new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(9, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star)]);
            Place(RequestEditor, 0, 0);
            Place(Splitter, 0, 1);
            Place(SplitterLine, 0, 1);
            Place(ResponsePane, 0, 2);
            Place(ResponseBar, 0, 2);
            Splitter.ResizeDirection = GridResizeDirection.Columns;
            SplitterLine.Width = 1;
            SplitterLine.Height = double.NaN;
            SplitterLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SplitterLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ResponsePane.Margin = new Thickness(4, 0, 0, 8);
        }
        else
        {
            SplitGrid.RowDefinitions.AddRange(collapsed
                ? [new RowDefinition(1, GridUnitType.Star), new RowDefinition(0, GridUnitType.Pixel), new RowDefinition(ResponseBarHeight, GridUnitType.Pixel)]
                : [new RowDefinition(1 - responseShare, GridUnitType.Star), new RowDefinition(9, GridUnitType.Pixel), new RowDefinition(responseShare, GridUnitType.Star)]);
            Place(RequestEditor, 0, 0);
            Place(Splitter, 1, 0);
            Place(SplitterLine, 1, 0);
            Place(ResponsePane, 2, 0);
            Place(ResponseBar, 2, 0);
            Splitter.ResizeDirection = GridResizeDirection.Rows;
            SplitterLine.Height = 1;
            SplitterLine.Width = double.NaN;
            SplitterLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            SplitterLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ResponsePane.Margin = new Thickness(0, 4, 0, 8);
        }
        // Collapsed, there is no split to drag: the bar is the pane.
        Splitter.IsVisible = !collapsed;
        SplitterLine.IsVisible = !collapsed;

        static void Place(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
        }
    }
}
