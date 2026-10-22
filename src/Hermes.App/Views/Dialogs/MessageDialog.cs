using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hermes.App.Views.Dialogs;

/// <summary>Simple modal with a message and buttons; returns the id of the clicked button.</summary>
public sealed class MessageDialog : Window
{
    public MessageDialog() : this("", "", [("OK", "ok", "accent")]) { }

    /// <param name="buttons">(label, result id, style class)</param>
    public MessageDialog(string title, string message, IEnumerable<(string Label, string Id, string Class)> buttons)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        foreach (var (label, id, cls) in buttons)
        {
            var button = new Button { Content = label };
            if (cls.Length > 0)
                button.Classes.Add(cls);
            button.Click += (_, _) => Close(id);
            buttonPanel.Children.Add(button);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttonPanel,
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                Close("cancel");
        };
    }
}
