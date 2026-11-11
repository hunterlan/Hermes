using Avalonia;
using Avalonia.Styling;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

public sealed class SettingsService
{
    private bool _failureReported;

    public AppSettings Current { get; private set; } = SettingsStore.Load();

    public event Action? Changed;

    /// <summary>Settings couldn't be written by <see cref="SaveCurrent"/>; raised once until a write works again.</summary>
    public event Action<StorageError>? StorageProblem;

    /// <summary>
    /// Writes <paramref name="settings"/> and makes them current. When they can't be written nothing changes and the
    /// error comes back for the caller to show; null when it worked.
    /// </summary>
    public StorageError? Update(AppSettings settings)
    {
        if (TryWrite(settings) is { } error)
            return error;
        _failureReported = false;
        Current = settings;
        ApplyTheme();
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Writes <see cref="Current"/> after a one-setting change made in place (theme, layout). The change stays for this
    /// session even if it can't be written; that is reported through <see cref="StorageProblem"/>, once.
    /// </summary>
    public void SaveCurrent()
    {
        if (TryWrite(Current) is { } error)
        {
            if (!_failureReported)
            {
                _failureReported = true;
                StorageProblem?.Invoke(error);
            }
        }
        else
        {
            _failureReported = false;
        }
        ApplyTheme();
        Changed?.Invoke();
    }

    private static StorageError? TryWrite(AppSettings settings)
    {
        try
        {
            SettingsStore.Save(settings);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StorageErrorDescriber.Describe(ex, AppPaths.SettingsFile, "Settings", StorageOperation.Write);
        }
    }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Current.Theme switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    public void ToggleTheme()
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Current.Theme = isDark ? ThemePreference.Light : ThemePreference.Dark;
        SaveCurrent();
    }
}
