using System.ComponentModel;
using System.Windows.Input;
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
    private bool _quitConfirmed;

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
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CommandPalette.PropertyChanged -= OnPaletteChanged;
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CommandPalette.PropertyChanged += OnPaletteChanged;
            SidebarColumn.Width = new GridLength(_viewModel.SidebarWidth);
        }
    }

    // ---- command palette ----

    private void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && _viewModel?.CommandPalette.IsOpen == true)
            Dispatcher.UIThread.Post(() => PaletteBox.Focus());
        else if (e.PropertyName == nameof(CommandPaletteViewModel.Selected) && _viewModel?.CommandPalette.Selected is { } selected)
            PaletteList.ScrollIntoView(selected);
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.CommandPalette is not { } palette)
            return;
        switch (e.Key)
        {
            case Key.Down:
                palette.Move(1);
                break;
            case Key.Up:
                palette.Move(-1);
                break;
            case Key.Enter:
                palette.Run();
                break;
            case Key.Escape:
                palette.Close();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: PaletteItem item })
            _viewModel?.CommandPalette.Run(item);
    }

    private void OnPaletteBackdropPressed(object? sender, PointerPressedEventArgs e) => _viewModel?.CommandPalette.Close();

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

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_viewModel is null)
            return;
        // The OS won't wait for a prompt when it shuts down.
        if (_viewModel.HasUnsavedEdits && !_quitConfirmed && e.CloseReason != WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
            if (!await _viewModel.ConfirmQuitAsync())
                return;
            _quitConfirmed = true;
            Close();
            return;
        }

        _sessionTimer.Stop();
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
                Run(vm.SendActiveCommand);
                break;
            case Key.S:
                Run(vm.SaveActiveCommand);
                break;
            case Key.T or Key.N when !shift:
                Run(vm.NewRequestCommand);
                break;
            case Key.W:
                Run(vm.CloseTabCommand);
                break;
            case Key.O:
                Run(vm.OpenImportCommand);
                break;
            case Key.OemComma:
                Run(vm.OpenSettingsCommand);
                break;
            case Key.OemBackslash or Key.OemPipe:
                Run(vm.ToggleSidebarCommand);
                break;
            case Key.K:
                Run(vm.OpenCommandPaletteCommand);
                break;
            case Key.E:
                EnvironmentButton.Flyout?.ShowAt(EnvironmentButton);
                break;
            case Key.L:
                this.GetVisualDescendants().OfType<RequestTabView>().FirstOrDefault()?.FocusUrl();
                break;
            case Key.Tab:
                Run(shift ? vm.PreviousTabCommand : vm.NextTabCommand);
                break;
            case Key.PageDown:
                Run(vm.NextTabCommand);
                break;
            case Key.PageUp:
                Run(vm.PreviousTabCommand);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Shortcuts follow the same rules as the buttons bound to the same commands.
    private static void Run(ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }
}
