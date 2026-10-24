using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

internal static class UiTestHelpers
{
    /// <summary>Screenshots go next to the test binaries unless HERMES_SCREENSHOT_DIR is set.</summary>
    public static string ScreenshotDirectory { get; } =
        Environment.GetEnvironmentVariable("HERMES_SCREENSHOT_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "screenshots");

    /// <summary>
    /// Opens a main window with a fresh view model (tests run in random order and the DI singleton would leak
    /// tabs between them), focused on a new request tab.
    /// </summary>
    public static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
    {
        var vm = new MainWindowViewModel(App.Services.GetRequiredService<TabServices>());
        vm.NewRequestCommand.Execute(null);
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 860 };
        App.Services.GetRequiredService<Services.DialogService>().Owner = window;
        window.Show();
        Pump();
        return (window, vm);
    }

    public static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Pumps the UI thread until <paramref name="condition"/> holds; fails after 5 seconds.</summary>
    public static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            Pump();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    public static string Screenshot(Window window, string name)
    {
        Pump();
        Directory.CreateDirectory(ScreenshotDirectory);
        var path = Path.Combine(ScreenshotDirectory, name + ".png");
        window.CaptureRenderedFrame()?.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return path;
    }
}
