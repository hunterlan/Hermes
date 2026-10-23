using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private DataFolderLock? _dataFolderLock;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();
        Services.GetRequiredService<SettingsService>().ApplyTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before anything opens the folder's files or its keychain entry.
            var dataDirectory = DataDirectoryOf(Services.GetRequiredService<SettingsService>());
            _dataFolderLock = DataFolderLock.TryAcquire(dataDirectory);
            if (_dataFolderLock is null)
            {
                desktop.MainWindow = new MessageDialog("Hermes is already running",
                    $"Another Hermes window is using the data folder {dataDirectory}. Switch to that window, or close it and start Hermes again.",
                    [("OK", "ok", "accent")]);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
            Services.GetRequiredService<DialogService>().Owner = window;
            desktop.MainWindow = window;
            // Exit, not ShutdownRequested: a shutdown request can still be cancelled.
            desktop.Exit += (_, _) =>
            {
                // Give queued history writes a moment to land; a slow disk mustn't hold the app open.
                Services.GetRequiredService<WorkspaceService>().FlushHistoryAsync().Wait(TimeSpan.FromSeconds(2));
                (Services as IDisposable)?.Dispose();
                _dataFolderLock.Dispose();
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
            var dataDir = DataDirectoryOf(sp.GetRequiredService<SettingsService>());
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

    private static string DataDirectoryOf(SettingsService settings) =>
        string.IsNullOrWhiteSpace(settings.Current.DataDirectory) ? AppPaths.DefaultDataDirectory : settings.Current.DataDirectory;
}
