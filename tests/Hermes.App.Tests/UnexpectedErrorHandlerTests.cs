using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;

namespace Hermes.App.Tests;

public class UnexpectedErrorHandlerTests
{
    private static StorageErrorDialog? DialogOf(Window owner) =>
        owner.OwnedWindows.OfType<StorageErrorDialog>().SingleOrDefault();

    [AvaloniaFact]
    public async Task A_bug_is_shown_as_something_went_wrong_with_the_exception_under_details_and_copy_details()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var handler = new UnexpectedErrorHandler(new DialogService { Owner = window });

        var carryOn = handler.OnUiThreadException(new InvalidOperationException("the cat is out of the bag"));
        await UiTestHelpers.WaitUntil(() => DialogOf(window) is not null);

        Assert.True(carryOn);
        var dialog = DialogOf(window)!;
        Assert.Equal("Something went wrong", dialog.Title);
        var viewModel = (StorageErrorDialogViewModel)dialog.DataContext!;
        Assert.Contains("the cat is out of the bag", viewModel.DetailsText);
        Assert.Contains("InvalidOperationException", viewModel.DetailsText);
        Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Copy details"));
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_error_that_repeats_does_not_pile_dialogs_up()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var handler = new UnexpectedErrorHandler(new DialogService { Owner = window });

        handler.OnUiThreadException(new InvalidOperationException("first"));
        handler.OnUiThreadException(new InvalidOperationException("second"));
        handler.OnUiThreadException(new InvalidOperationException("third"));
        await UiTestHelpers.WaitUntil(() => DialogOf(window) is not null);
        UiTestHelpers.Pump();

        var dialog = DialogOf(window)!;
        Assert.Contains("first", ((StorageErrorDialogViewModel)dialog.DataContext!).DetailsText);
        dialog.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Running_out_of_memory_is_left_to_end_the_app()
    {
        var window = new Window();
        var handler = new UnexpectedErrorHandler(new DialogService { Owner = window });

        Assert.False(handler.OnUiThreadException(new OutOfMemoryException()));
        UiTestHelpers.Pump();

        Assert.Null(DialogOf(window));
    }

    [AvaloniaFact]
    public async Task Without_a_window_nothing_is_shown_and_nothing_is_thrown()
    {
        var handler = new UnexpectedErrorHandler(new DialogService());

        Assert.True(handler.OnUiThreadException(new InvalidOperationException("early")));
        UiTestHelpers.Pump();
        await Task.Delay(20, TestContext.Current.CancellationToken);
        UiTestHelpers.Pump();
    }

    [AvaloniaFact]
    public async Task An_unobserved_task_error_is_ignored_without_a_dialog()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("in a task")));

        UnexpectedErrorHandler.IgnoreUnobservedTaskException(args);
        UiTestHelpers.Pump();
        await Task.Delay(100);
        UiTestHelpers.Pump();

        Assert.True(args.Observed);
        Assert.Null(DialogOf(window));
        window.Close();
    }
}
