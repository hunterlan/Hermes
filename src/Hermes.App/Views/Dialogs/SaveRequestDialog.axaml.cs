using Avalonia.Controls;
using Avalonia.Interactivity;
using Hermes.App.ViewModels;

namespace Hermes.App.Views.Dialogs;

public partial class SaveRequestDialog : Window
{
    public SaveRequestDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SaveRequestDialogViewModel vm)
            vm.CloseRequested += result => Close(result);
    }

    private void CancelNewCollection(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveRequestDialogViewModel vm)
            vm.IsCreatingCollection = false;
    }
}
