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
        };
        buttonPanel.Bind(StackPanel.SpacingProperty, this.GetResourceObservable("Spacing8"));
        foreach (var (label, id, cls) in buttons)
        {
            var button = new Button { Content = label };
            if (cls.Length > 0)
                button.Classes.Add(cls);
            button.Click += (_, _) => Close(id);
            buttonPanel.Children.Add(button);
        }

        var titleText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold };
        titleText.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FontSizeTitle"));

        var layout = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                titleText,
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttonPanel,
            },
        };
        layout.Bind(StackPanel.SpacingProperty, this.GetResourceObservable("Spacing14"));
        Content = layout;
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                Close("cancel");
        };
    }
}
