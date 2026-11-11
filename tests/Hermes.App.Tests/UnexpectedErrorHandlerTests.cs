using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

public class UnexpectedErrorHandlerTests
{
    private static StorageErrorDialog? DialogOf(Window owner) =>
        owner.OwnedWindows.OfType<StorageErrorDialog>().SingleOrDefault();

    private static (Window Window, UnexpectedErrorHandler Handler, Func<int> Quits) Create(bool withWindow = true)
    {
        var window = new Window { Width = 800, Height = 600 };
        if (withWindow)
            window.Show();
        var quits = 0;
        var handler = new UnexpectedErrorHandler(withWindow ? new DialogService { Owner = window } : new DialogService(), () => quits++);
        return (window, handler, () => quits);
    }

    [AvaloniaFact]
    public async Task A_bug_is_shown_with_details_and_copy_details_and_the_app_quits_when_it_closes()
    {
        var (window, handler, quits) = Create();

        var handled = handler.OnUiThreadException(new InvalidOperationException("the cat is out of the bag"));
        await UiTestHelpers.WaitUntil(() => DialogOf(window) is not null);

        Assert.True(handled);
        Assert.True(handler.IsQuitting);
        Assert.Equal(0, quits());
        var dialog = DialogOf(window)!;
        Assert.Equal("Something went wrong", dialog.Title);
        var viewModel = (StorageErrorDialogViewModel)dialog.DataContext!;
        Assert.Contains("the cat is out of the bag", viewModel.DetailsText);
        var buttons = dialog.GetVisualDescendants().OfType<Button>().Select(b => b.Content).ToList();
        Assert.Contains("Copy details", buttons);
        Assert.Contains("Quit", buttons);
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain(texts, text => text?.Contains("keep running") == true);
        Assert.Contains(texts, text => text?.Contains("Hermes will close") == true);

        dialog.Close();
        await UiTestHelpers.WaitUntil(() => quits() == 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_second_error_while_the_dialog_is_open_is_ignored()
    {
        var (window, handler, quits) = Create();

        handler.Fail(new InvalidOperationException("first"));
        handler.Fail(new InvalidOperationException("second"));
        handler.OnUiThreadException(new InvalidOperationException("third"));
        await UiTestHelpers.WaitUntil(() => DialogOf(window) is not null);
        UiTestHelpers.Pump();

        var dialog = DialogOf(window)!;
        Assert.Contains("first", ((StorageErrorDialogViewModel)dialog.DataContext!).DetailsText);
        dialog.Close();
        await UiTestHelpers.WaitUntil(() => quits() >= 1);
        UiTestHelpers.Pump();
        Assert.Equal(1, quits());
        window.Close();
    }

    [AvaloniaFact]
    public void Running_out_of_memory_is_left_to_end_the_process()
    {
        var (window, handler, quits) = Create();

        Assert.False(handler.OnUiThreadException(new OutOfMemoryException()));
        UiTestHelpers.Pump();

        Assert.Null(DialogOf(window));
        Assert.Equal(0, quits());
        window.Close();
    }

    [AvaloniaFact]
    public async Task When_the_dialog_cannot_be_shown_the_app_quits_anyway()
    {
        var (_, handler, quits) = Create(withWindow: false);

        Assert.True(handler.OnUiThreadException(new InvalidOperationException("early")));

        await UiTestHelpers.WaitUntil(() => quits() == 1);
    }

    [AvaloniaFact]
    public async Task An_unobserved_task_error_is_ignored_without_a_dialog_or_a_quit()
    {
        var (window, handler, quits) = Create();
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("in a task")));

        UnexpectedErrorHandler.IgnoreUnobservedTaskException(args);
        UiTestHelpers.Pump();
        await Task.Delay(100);
        UiTestHelpers.Pump();

        Assert.True(args.Observed);
        Assert.False(handler.IsQuitting);
        Assert.Null(DialogOf(window));
        Assert.Equal(0, quits());
        window.Close();
    }
}
