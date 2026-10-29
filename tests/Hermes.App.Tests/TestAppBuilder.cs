using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Hermes.App.Tests.TestAppBuilder))]

namespace Hermes.App.Tests;

public static class TestAppBuilder
{
    /// <summary>Isolated config/data directory so tests never touch the user's real workspace.</summary>
    public static string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "hermes-ui-tests-" + Guid.NewGuid().ToString("N"));

    public static AppBuilder BuildAvaloniaApp()
    {
        Environment.SetEnvironmentVariable("HERMES_CONFIG_DIR", DataDirectory);
        Environment.SetEnvironmentVariable("HERMES_SECRET_STORE", "memory");
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
