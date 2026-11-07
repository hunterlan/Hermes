using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class ExportHeaderNoticeTests
{
    /// <summary>Opens the notice over a fresh window and returns its dialog and the pending answer.</summary>
    private static (Window Window, MessageDialog Dialog, Task<bool> Answer) OpenNotice()
    {
        var (window, _) = UiTestHelpers.ShowMainWindow();
        var answer = App.Services.GetRequiredService<DialogService>().ConfirmExportWithInheritedHeadersAsync();
        UiTestHelpers.Pump();
        return (window, Assert.IsType<MessageDialog>(Assert.Single(window.OwnedWindows)), answer);
    }

    [AvaloniaFact]
    public async Task Notice_explains_the_copy_and_offers_export_or_cancel()
    {
        var (window, dialog, answer) = OpenNotice();

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains(texts, text => text!.Contains("Postman has no folder headers", StringComparison.Ordinal));
        var buttons = dialog.GetVisualDescendants().OfType<Button>().Select(button => (string?)button.Content).ToList();
        Assert.Equal(["Export", "Cancel"], buttons);

        dialog.Close("cancel");
        Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Escape_cancels_the_export()
    {
        var (window, dialog, answer) = OpenNotice();

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Cancel_button_cancels_the_export()
    {
        var (window, dialog, answer) = OpenNotice();

        ClickButton(dialog, "Cancel");
        UiTestHelpers.Pump();

        Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Export_button_continues_the_export()
    {
        var (window, dialog, answer) = OpenNotice();

        ClickButton(dialog, "Export");
        UiTestHelpers.Pump();

        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
        window.Close();
    }

    private static void ClickButton(MessageDialog dialog, string label)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(candidate => (string?)candidate.Content == label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
