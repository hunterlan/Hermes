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
    }

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
            _tab.SettingsRevealRequested -= ScrollSettingsIntoView;
            _tab.HeadersRevealRequested -= ScrollHeadersIntoView;
            _tab.BodyRevealRequested -= ScrollBodyIntoView;
            _tab.UrlFocusRequested -= FocusUrl;
        }
        _tab = DataContext as RequestTabViewModel;
        if (_tab is not null)
        {
            _tab.SettingsRevealRequested += ScrollSettingsIntoView;
            _tab.HeadersRevealRequested += ScrollHeadersIntoView;
            _tab.BodyRevealRequested += ScrollBodyIntoView;
            _tab.UrlFocusRequested += FocusUrl;
        }
    }

    /// <summary>Waits for the section to be laid out (it may have just become visible), then scrolls the request editor to it.</summary>
    private void ScrollSettingsIntoView() =>
        Dispatcher.UIThread.Post(() => SettingsSection.BringIntoView(), DispatcherPriority.Loaded);

    private void ScrollHeadersIntoView() =>
        Dispatcher.UIThread.Post(() => HeadersSection.BringIntoView(), DispatcherPriority.Loaded);

    private void ScrollBodyIntoView() =>
        Dispatcher.UIThread.Post(() => BodySection.BringIntoView(), DispatcherPriority.Loaded);

    public void FocusUrl() => UrlBox.FocusEditor();

    /// <summary>Response below the request (vertical) or to its right (horizontal).</summary>
    private void ApplyLayout()
    {
        var horizontal = _settings?.Current.Layout == ResponseLayout.Horizontal;
        SplitGrid.RowDefinitions.Clear();
        SplitGrid.ColumnDefinitions.Clear();
        if (horizontal)
        {
            SplitGrid.ColumnDefinitions.AddRange([new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(9, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star)]);
            Place(RequestEditor, 0, 0);
            Place(Splitter, 0, 1);
            Place(SplitterLine, 0, 1);
            Place(ResponsePane, 0, 2);
            Splitter.ResizeDirection = GridResizeDirection.Columns;
            SplitterLine.Width = 1;
            SplitterLine.Height = double.NaN;
            SplitterLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            SplitterLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            ResponsePane.Margin = new Thickness(4, 0, 0, 8);
        }
        else
        {
            SplitGrid.RowDefinitions.AddRange([new RowDefinition(1, GridUnitType.Star), new RowDefinition(9, GridUnitType.Pixel), new RowDefinition(1, GridUnitType.Star)]);
            Place(RequestEditor, 0, 0);
            Place(Splitter, 1, 0);
            Place(SplitterLine, 1, 0);
            Place(ResponsePane, 2, 0);
            Splitter.ResizeDirection = GridResizeDirection.Rows;
            SplitterLine.Height = 1;
            SplitterLine.Width = double.NaN;
            SplitterLine.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            SplitterLine.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            ResponsePane.Margin = new Thickness(0, 4, 0, 8);
        }

        static void Place(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
        }
    }
}
