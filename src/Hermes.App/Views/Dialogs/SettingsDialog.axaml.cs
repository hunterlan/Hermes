using Avalonia.Controls;
using Hermes.App.ViewModels;

namespace Hermes.App.Views.Dialogs;

public partial class SettingsDialog : Window
{
    public SettingsDialog() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsDialogViewModel vm)
            vm.CloseRequested += saved => Close(saved);
    }
}
