using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();
        Services.GetRequiredService<SettingsService>().ApplyTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
            Services.GetRequiredService<DialogService>().Owner = window;
            desktop.MainWindow = window;
            // Exit, not ShutdownRequested: a shutdown request can still be cancelled.
            desktop.Exit += (_, _) =>
            {
                // Give queued history writes a moment to land; a slow disk mustn't hold the app open.
                Services.GetRequiredService<WorkspaceService>().FlushHistoryAsync().Wait(TimeSpan.FromSeconds(2));
                (Services as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>().Current;
            var dataDir = string.IsNullOrWhiteSpace(settings.DataDirectory) ? AppPaths.DefaultDataDirectory : settings.DataDirectory;
            return new WorkspaceRepository(dataDir, KeychainSecretStore.Create(dataDir));
        });
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<HttpClientProvider>();
        services.AddSingleton<RequestExecutor>();
        services.AddSingleton<OAuth2Service>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<TabServices>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
