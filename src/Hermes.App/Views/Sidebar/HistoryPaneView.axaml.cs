using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermes.App.ViewModels.Sidebar;

namespace Hermes.App.Views.Sidebar;

public partial class HistoryPaneView : UserControl
{
    public HistoryPaneView() => InitializeComponent();

    private HistoryPaneViewModel? ViewModel => DataContext as HistoryPaneViewModel;

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: HistoryItemViewModel item })
            ViewModel?.Open(item);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: HistoryItemViewModel item })
            ViewModel?.DeleteCommand.Execute(item);
        e.Handled = true;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: HistoryItemViewModel item } control || ViewModel is not { } vm)
            return;
        var open = new MenuItem { Header = "Open in new tab" };
        open.Click += (_, _) => vm.Open(item);
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => vm.DeleteCommand.Execute(item);
        new ContextMenu { ItemsSource = new List<Control> { open, delete } }.Open(control);
        e.Handled = true;
    }
}
