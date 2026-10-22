using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class TabHeader : UserControl
{
    public TabHeader() => InitializeComponent();

    private TabViewModelBase? Tab => DataContext as TabViewModelBase;

    private MainWindowViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Tab is { } tab && Main is { } main)
            main.SelectTabCommand.Execute(tab);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Middle click closes, like browsers and Postman.
        if (e.InitialPressMouseButton == MouseButton.Middle && Tab is { } tab && Main is { } main)
            _ = main.CloseTabCommand.ExecuteAsync(tab);
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (Tab is { } tab && Main is { } main)
            _ = main.CloseTabCommand.ExecuteAsync(tab);
        e.Handled = true;
    }

    private void OnCloseOthers(object? sender, RoutedEventArgs e)
    {
        if (Tab is { } tab && Main is { } main)
            _ = main.CloseOtherTabsCommand.ExecuteAsync(tab);
    }

    private void OnCloseAll(object? sender, RoutedEventArgs e) => _ = Main?.CloseAllTabsCommand.ExecuteAsync(null);

    private void OnDuplicate(object? sender, RoutedEventArgs e)
    {
        if (Tab is { } tab)
            Main?.DuplicateTabCommand.Execute(tab);
    }
}
