using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so shortcuts work even when a text editor has focus (AvaloniaEdit swallows Ctrl+Enter, Ctrl+S, ...).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
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
