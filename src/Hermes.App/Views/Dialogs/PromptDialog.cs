using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hermes.App.Views.Dialogs;

/// <summary>Asks for a single line of text; returns null when cancelled.</summary>
public sealed class PromptDialog : Window
{
    private readonly TextBox _input;

    public PromptDialog() : this("", "", "", "OK") { }

    public PromptDialog(string title, string label, string initial, string okText)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _input = new TextBox { Text = initial };
        var ok = new Button { Content = okText, IsDefault = true };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Submit();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = label, Classes = { "secondary" } },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 6, 0, 0),
                    Children = { cancel, ok },
                },
            },
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Submit();
        };
        Opened += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Submit()
    {
        var text = _input.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
            Close(text);
    }
}
