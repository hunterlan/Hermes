using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>When a storage problem is shown: kept until the window opens, dropped once it has closed.</summary>
public class StorageProblemTimingTests
{
    private static readonly StorageError Problem = StorageError.Plain("Something couldn't be saved.");

    private sealed record WindowFixture(Window Window, MainWindowViewModel ViewModel);

    private static WindowFixture Create()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        App.Services.GetRequiredService<DialogService>().Owner = window;
        return new WindowFixture(window, new MainWindowViewModel(App.Services.GetRequiredService<TabServices>()));
    }

    private static int DialogCount(Window window) => window.OwnedWindows.OfType<StorageErrorDialog>().Count();

    [AvaloniaFact]
    public void A_problem_reported_before_the_window_opens_is_shown_when_it_opens()
    {
        var (window, viewModel) = Create();

        viewModel.ReportStorageProblem(Problem);
        viewModel.ReportStorageProblem(Problem);
        UiTestHelpers.Pump();
        Assert.Equal(0, DialogCount(window));

        viewModel.OnWindowOpened();
        UiTestHelpers.Pump();

        Assert.Equal(1, DialogCount(window));
        window.OwnedWindows.OfType<StorageErrorDialog>().Single().Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_problem_is_shown_at_once_while_the_window_is_open()
    {
        var (window, viewModel) = Create();
        viewModel.OnWindowOpened();

        viewModel.ReportStorageProblem(Problem);
        UiTestHelpers.Pump();

        Assert.Equal(1, DialogCount(window));
        window.OwnedWindows.OfType<StorageErrorDialog>().Single().Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_problem_reported_after_the_window_closed_is_dropped()
    {
        var (window, viewModel) = Create();
        viewModel.OnWindowOpened();
        viewModel.OnWindowClosed();

        viewModel.ReportStorageProblem(Problem);
        UiTestHelpers.Pump();

        Assert.Equal(0, DialogCount(window));
        window.Close();
    }
}
