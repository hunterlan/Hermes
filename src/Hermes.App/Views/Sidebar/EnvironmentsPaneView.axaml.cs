using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hermes.App.ViewModels.Sidebar;

namespace Hermes.App.Views.Sidebar;

public partial class EnvironmentsPaneView : UserControl
{
    public static readonly IValueConverter BoldIf = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);

    public EnvironmentsPaneView() => InitializeComponent();

    private EnvironmentsPaneViewModel? ViewModel => DataContext as EnvironmentsPaneViewModel;

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: EnvironmentItemViewModel { IsRenaming: false } item })
            ViewModel?.Open(item);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: EnvironmentItemViewModel item } control)
        {
            ShowMenu(control, item);
            e.Handled = true;
        }
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: EnvironmentItemViewModel item } control)
            ShowMenu(control, item);
        e.Handled = true;
    }

    private void ShowMenu(Control anchor, EnvironmentItemViewModel item)
    {
        if (ViewModel is not { } vm)
            return;

        MenuItem Item(string header, Action action)
        {
            var menuItem = new MenuItem { Header = header };
            menuItem.Click += (_, _) => action();
            return menuItem;
        }

        var items = new List<Control> { Item("Open", () => vm.Open(item)) };
        if (item.IsRegular)
        {
            items.Add(Item(item.IsActive ? "Deactivate" : "Set active", () => vm.SetActiveCommand.Execute(item)));
            items.Add(Item("Rename", () => vm.RenameCommand.Execute(item)));
        }
        items.Add(Item("Duplicate", () => vm.DuplicateCommand.Execute(item)));
        items.Add(Item("Export", () => vm.ExportCommand.Execute(item)));
        if (item.IsRegular)
        {
            items.Add(new Separator());
            items.Add(Item("Delete", () => vm.DeleteCommand.Execute(item)));
        }
        new ContextMenu { ItemsSource = items }.Open(anchor);
    }

    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            box.Text = (box.DataContext as EnvironmentItemViewModel)?.Name;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: EnvironmentItemViewModel item } box || ViewModel is not { } vm)
            return;
        if (e.Key is Key.Enter or Key.Escape)
        {
            vm.CommitRename(item, e.Key == Key.Enter ? box.Text : null);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: EnvironmentItemViewModel { IsRenaming: true } item } box)
            ViewModel?.CommitRename(item, box.Text);
    }
}
