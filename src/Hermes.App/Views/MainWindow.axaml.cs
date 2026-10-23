using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so shortcuts work even when a text editor has focus (AvaloniaEdit swallows Ctrl+Enter, Ctrl+S, ...).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _sessionTimer.Tick += (_, _) => _viewModel?.SaveSession();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private ColumnDefinition SidebarColumn => BodyGrid.ColumnDefinitions[0];

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SidebarColumn.Width = new GridLength(_viewModel.SidebarWidth);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsSidebarVisible) || _viewModel is null)
            return;
        // The splitter sets a pixel width on the column, so hiding the pane must collapse the column too.
        if (_viewModel.IsSidebarVisible)
        {
            SidebarColumn.Width = new GridLength(_viewModel.SidebarWidth);
        }
        else
        {
            _viewModel.SidebarWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : _viewModel.SidebarWidth;
            SidebarColumn.Width = new GridLength(0);
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _sessionTimer.Start();
        var workspace = App.Services?.GetService<WorkspaceService>();
        if (workspace?.LoadErrors is { Count: > 0 } errors)
        {
            var dialogs = App.Services!.GetRequiredService<DialogService>();
            await dialogs.ShowMessageAsync("Some files couldn't be loaded",
                "These files are corrupted or not in the expected format and were skipped:\n\n" + string.Join("\n", errors));
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _sessionTimer.Stop();
        if (_viewModel is null)
            return;
        if (_viewModel.IsSidebarVisible && SidebarColumn.ActualWidth > 0)
            _viewModel.SidebarWidth = SidebarColumn.ActualWidth;
        _viewModel.SaveSession();
    }

    // ---- drop files anywhere to import ----

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.Copy;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_viewModel is null || !e.DataTransfer.Contains(DataFormat.File))
            return;
        var files = (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (files.Count > 0)
            _ = _viewModel.OpenImportCommand.ExecuteAsync(files);
    }

    // ---- shortcuts ----

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } vm)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!ctrl)
            return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Enter:
                _ = vm.SendActiveCommand.ExecuteAsync(null);
                break;
            case Key.S:
                _ = vm.SaveActiveCommand.ExecuteAsync(null);
                break;
            case Key.T or Key.N when !shift:
                vm.NewRequestCommand.Execute(null);
                break;
            case Key.W:
                _ = vm.CloseTabCommand.ExecuteAsync(null);
                break;
            case Key.O:
                _ = vm.OpenImportCommand.ExecuteAsync(null);
                break;
            case Key.OemComma:
                _ = vm.OpenSettingsCommand.ExecuteAsync(null);
                break;
            case Key.OemBackslash or Key.OemPipe:
                vm.ToggleSidebarCommand.Execute(null);
                break;
            case Key.E:
                EnvironmentButton.Flyout?.ShowAt(EnvironmentButton);
                break;
            case Key.L:
                this.GetVisualDescendants().OfType<RequestTabView>().FirstOrDefault()?.FocusUrl();
                break;
            case Key.Tab:
                (shift ? vm.PreviousTabCommand : vm.NextTabCommand).Execute(null);
                break;
            case Key.PageDown:
                vm.NextTabCommand.Execute(null);
                break;
            case Key.PageUp:
                vm.PreviousTabCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
