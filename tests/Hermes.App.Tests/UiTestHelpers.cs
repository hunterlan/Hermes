using System.Net;
using System.Net.Sockets;
using System.Text;
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

    public static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
    {
        var vm = App.Services.GetRequiredService<MainWindowViewModel>();
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
        window.CaptureRenderedFrame()?.Save(path);
        return path;
    }
}

/// <summary>Minimal local HTTP server returning a JSON body.</summary>
internal sealed class JsonServer : IDisposable
{
    private readonly HttpListener _listener = new();

    public JsonServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                var body = Encoding.UTF8.GetBytes(
                    $"{{\"method\":\"{ctx.Request.HttpMethod}\",\"path\":\"{ctx.Request.Url!.PathAndQuery}\",\"items\":[1,2,3],\"nested\":{{\"ok\":true}}}}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.AppendHeader("Set-Cookie", "session=abc123; Path=/");
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        });
    }

    public string Url { get; }

    public void Dispose() => _listener.Close();
}
