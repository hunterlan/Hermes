using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hermes.App.Views.Dialogs;

public partial class CodeSnippetDialog : Window
{
    public CodeSnippetDialog() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
