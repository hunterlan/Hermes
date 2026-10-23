using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Views;

public partial class RequestTabView : UserControl
{
    private SettingsService? _settings;
    private RequestTabViewModel? _viewModel;

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
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as RequestTabViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestTabViewModel.IsRenaming) && _viewModel?.IsRenaming == true)
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

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

    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => _viewModel?.EndRenameCommand.Execute(null);

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            _viewModel?.EndRenameCommand.Execute(null);
            e.Handled = true;
        }
    }
}
