using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.TestSupport;
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

    public static string Screenshot(Window window, string name)
    {
        Pump();
        Directory.CreateDirectory(ScreenshotDirectory);
        var path = Path.Combine(ScreenshotDirectory, name + ".png");
        window.CaptureRenderedFrame()?.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return path;
    }
}

/// <summary>Minimal local HTTP server returning a JSON body.</summary>
internal sealed class JsonServer : IDisposable
{
    private readonly LocalHttpServer _server = new(async (ctx, stop) =>
    {
        var body = Encoding.UTF8.GetBytes(
            $"{{\"method\":\"{ctx.Request.HttpMethod}\",\"path\":\"{ctx.Request.Url!.PathAndQuery}\",\"items\":[1,2,3],\"nested\":{{\"ok\":true}}}}");
        ctx.Response.ContentType = "application/json";
        ctx.Response.AppendHeader("Set-Cookie", "session=abc123; Path=/");
        await ctx.Response.OutputStream.WriteAsync(body, stop);
        ctx.Response.Close();
    });

    public string Url => _server.Url;

    public void Dispose() => _server.Dispose();
}
