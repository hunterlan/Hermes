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

    private double ResponseStripWidth => (double)this.FindResource("ResponseStripWidth")!;

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
            _tab.Response.Pane.PropertyChanged -= OnResponsePropertyChanged;
        }
        _tab = DataContext as RequestTabViewModel;
        if (_tab is not null)
        {
            _tab.Sections.SettingsRevealRequested += ScrollSettingsIntoView;
            _tab.Sections.HeadersRevealRequested += ScrollHeadersIntoView;
            _tab.Sections.BodyRevealRequested += ScrollBodyIntoView;
            _tab.UrlEditor.FocusRequested += FocusUrl;
            _tab.Response.Pane.PropertyChanged += OnResponsePropertyChanged;
        }
        ApplyLayout();
    }

    private void OnResponsePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResponsePaneLayoutViewModel.IsResponseCollapsed)
            or nameof(ResponsePaneLayoutViewModel.ResponseSplitRatio)
            or nameof(ResponsePaneLayoutViewModel.SideBySideResponseSplitRatio))
            ApplyLayout();
    }

    /// <summary>
    /// Keeps the split the user dragged to, for the layout it was dragged in. Dragged down to the bar's height, or
    /// right to the strip's width, the pane collapses instead.
    /// </summary>
    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (_tab?.Response.Pane is not { IsResponseCollapsed: false } pane)
            return;
        // The splitter sets the two lengths in proportion to the sizes it dragged them to.
        var horizontal = IsSideBySide;
        var requestLength = horizontal ? SplitGrid.ColumnDefinitions[0].Width.Value : SplitGrid.RowDefinitions[0].Height.Value;
        var responseLength = horizontal ? SplitGrid.ColumnDefinitions[2].Width.Value : SplitGrid.RowDefinitions[2].Height.Value;
        var splitLength = horizontal
            ? SplitGrid.ColumnDefinitions[0].ActualWidth + SplitGrid.ColumnDefinitions[2].ActualWidth
            : SplitGrid.RowDefinitions[0].ActualHeight + SplitGrid.RowDefinitions[2].ActualHeight;
        if (requestLength + responseLength <= 0 || splitLength <= 0)
            return;
        var responseShare = responseLength / (requestLength + responseLength);
        var collapsedLength = horizontal ? ResponseStripWidth : ResponseBarHeight;
        if (responseShare * splitLength <= collapsedLength)
            // The share from before the drag stays, so expanding brings back the pane the user had.
            pane.IsResponseCollapsed = true;
        else if (horizontal)
            pane.SideBySideResponseSplitRatio = responseShare;
        else
            pane.ResponseSplitRatio = responseShare;
    }

    private void OnSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tab?.Response.Pane.ToggleCollapsedCommand is not { } toggle || !toggle.CanExecute(null))
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
    /// Response below the request (vertical) or to its right (horizontal). The rows or columns come from the tab's
    /// response pane, its share of the split for the layout, or the bar's height / strip's width while collapsed, so
    /// rebuilding them on a settings change keeps both.
    /// </summary>
    private void ApplyLayout()
    {
        var horizontal = IsSideBySide;
        var collapsed = _tab?.Response.Pane.IsResponseCollapsed == true;
        var responseShare = (horizontal ? _tab?.Response.Pane.SideBySideResponseSplitRatio : _tab?.Response.Pane.ResponseSplitRatio)
            ?? SessionTab.DefaultResponseSplitRatio;
        SplitGrid.RowDefinitions.Clear();
        SplitGrid.ColumnDefinitions.Clear();
        if (horizontal)
        {
            SplitGrid.ColumnDefinitions.AddRange(collapsed
                ? [new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(0, GridUnitType.Pixel), new ColumnDefinition(ResponseStripWidth, GridUnitType.Pixel)]
                : [new ColumnDefinition(1 - responseShare, GridUnitType.Star), new ColumnDefinition(9, GridUnitType.Pixel), new ColumnDefinition(responseShare, GridUnitType.Star)]);
            Place(RequestEditor, 0, 0);
            Place(Splitter, 0, 1);
            Place(SplitterLine, 0, 1);
            Place(ResponsePane, 0, 2);
            Place(ResponseBar, 0, 2);
            Place(ResponseStrip, 0, 2);
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
            Place(ResponseStrip, 2, 0);
            Splitter.ResizeDirection = GridResizeDirection.Rows;
            SplitterLine.Height = 1;
            SplitterLine.Width = double.NaN;
            SplitterLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            SplitterLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ResponsePane.Margin = new Thickness(0, 4, 0, 8);
        }
        // Collapsed, there is no split to drag: the bar or strip is the pane.
        Splitter.IsVisible = !collapsed;
        SplitterLine.IsVisible = !collapsed;

        static void Place(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
        }
    }
}
