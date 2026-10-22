using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermes.App.Views.Dialogs;

public partial class CookieManagerDialog : Window
{
    public CookieManagerDialog() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
