using Avalonia;
using Avalonia.Controls;
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
            StartMainWindow(desktop, showWindow: false);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Takes the data folder's lock and opens the main window; when the folder can't be used, shows why instead and
    /// lets the user pick another folder. <paramref name="showWindow"/> is true once the app is running, when setting
    /// <see cref="IClassicDesktopStyleApplicationLifetime.MainWindow"/> no longer shows the window by itself.
    /// </summary>
    private void StartMainWindow(IClassicDesktopStyleApplicationLifetime desktop, bool showWindow)
    {
        // Before anything opens the folder's files or its keychain entry.
        var dataDirectory = DataDirectoryOf(Services.GetRequiredService<SettingsService>());
        try
        {
            _dataFolderLock = DataFolderLock.TryAcquire(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStartupProblem(desktop, dataDirectory,
                StorageErrorDescriber.Describe(ex, dataDirectory, $"Data folder \"{dataDirectory}\"", StorageOperation.Use), showWindow);
            return;
        }
        if (_dataFolderLock is null)
        {
            var message = $"Another Hermes window is using the data folder {dataDirectory}. Switch to that window, or close it and start Hermes again.";
            SetMainWindow(desktop, new MessageDialog("Hermes can't start", message, [("OK", "ok", "accent")]), showWindow);
            return;
        }

        if (DataFolderCheck.FindUnusableSubfolder(dataDirectory) is { } unusableSubfolder)
        {
            // Released so the folder isn't held by a start that didn't happen.
            _dataFolderLock.Dispose();
            _dataFolderLock = null;
            ShowStartupProblem(desktop, dataDirectory, unusableSubfolder, showWindow);
            return;
        }

        var window = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
        Services.GetRequiredService<DialogService>().Owner = window;
        SetMainWindow(desktop, window, showWindow);
        // Exit, not ShutdownRequested: a shutdown request can still be cancelled.
        desktop.Exit += (_, _) =>
        {
            // Give queued history writes a moment to land; a slow disk mustn't hold the app open.
            // Blocking is deliberate: the app is exiting, there is no UI left to freeze, and the process
            // may end as soon as this handler returns, so the flush cannot be awaited asynchronously.
            Services.GetRequiredService<WorkspaceService>().FlushHistoryAsync().Wait(TimeSpan.FromSeconds(2));
            (Services as IDisposable)?.Dispose();
            _dataFolderLock!.Dispose();
        };
    }

    private void ShowStartupProblem(IClassicDesktopStyleApplicationLifetime desktop, string dataDirectory, StorageError error, bool showWindow)
    {
        var dialog = new StorageErrorDialog("Hermes can't start", [error],
            StartupFolderChoices.For(dataDirectory), closeText: "Quit");
        // The dialog is the only window; closing it to pick a folder mustn't end the app.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        dialog.Closed += (_, _) => OnStartupProblemClosed(desktop, dialog.Result);
        SetMainWindow(desktop, dialog, showWindow);
    }

    private void OnStartupProblemClosed(IClassicDesktopStyleApplicationLifetime desktop, StorageDialogResult? result)
    {
        if (result is null)
        {
            desktop.Shutdown();
            return;
        }
        var settings = Services.GetRequiredService<SettingsService>();
        var chosenFolder = result.ChoiceId == StartupFolderChoices.ChooseFolder ? result.Folder : null;
        try
        {
            settings.Current.DataDirectory = chosenFolder;
            settings.Update(settings.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failure = StorageErrorDescriber.Describe(ex, AppPaths.SettingsFile, "Settings", StorageOperation.Write);
            var notice = new StorageErrorDialog("Hermes can't start", [failure], [], closeText: "Quit");
            notice.Closed += (_, _) => desktop.Shutdown();
            SetMainWindow(desktop, notice, showWindow: true);
            return;
        }
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        StartMainWindow(desktop, showWindow: true);
    }

    private static void SetMainWindow(IClassicDesktopStyleApplicationLifetime desktop, Window window, bool showWindow)
    {
        desktop.MainWindow = window;
        if (showWindow)
            window.Show();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SettingsService>();
        services.AddSingleton(sp =>
        {
            var dataDir = DataDirectoryOf(sp.GetRequiredService<SettingsService>());
            return new WorkspaceRepository(dataDir, KeychainSecretStore.Create(dataDir), sp.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<HttpClientProvider>();
        services.AddSingleton<RequestExecutor>();
        services.AddSingleton<OAuth2Service>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ManualService>();
        services.AddSingleton<TabServices>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }

    private static string DataDirectoryOf(SettingsService settings) =>
        string.IsNullOrWhiteSpace(settings.Current.DataDirectory) ? AppPaths.DefaultDataDirectory : settings.Current.DataDirectory;
}
