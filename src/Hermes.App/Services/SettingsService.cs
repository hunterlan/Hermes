using Avalonia;
using Avalonia.Styling;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The app settings. They are written off the UI thread, one write at a time in the order asked for, from a copy taken
/// when the write is asked for; disposing waits for the writes still queued.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly BackgroundWriter _writes = new();

    private bool _failureReported;

    public AppSettings Current { get; private set; } = SettingsStore.Load();

    public event Action? Changed;

    /// <summary>Settings couldn't be written by <see cref="SaveCurrentAsync"/>; raised once until a write works again.</summary>
    public event Action<StorageError>? StorageProblem;

    /// <summary>
    /// Writes <paramref name="settings"/> and then makes them current. When they can't be written nothing changes and
    /// the error comes back for the caller to show; null when it worked. <paramref name="settings"/> must not be
    /// changed afterwards: it becomes <see cref="Current"/>.
    /// </summary>
    public async Task<StorageError?> UpdateAsync(AppSettings settings)
    {
        if (await TryWriteAsync(settings.Copy()) is { } error)
            return error;
        _failureReported = false;
        Current = settings;
        ApplyTheme();
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Writes <see cref="Current"/> after a one-setting change made in place (theme, layout). The change takes effect
    /// (and <see cref="Changed"/> is raised) before this returns, and stays for this session even if it can't be
    /// written; that is reported through <see cref="StorageProblem"/>, once. The task completes once it is written.
    /// </summary>
    public async Task SaveCurrentAsync()
    {
        var snapshot = Current.Copy();
        ApplyTheme();
        Changed?.Invoke();
        if (await TryWriteAsync(snapshot) is { } error)
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
    }

    /// <summary>
    /// <see cref="SaveCurrentAsync"/> for a UI handler that can't wait. An unexpected error still reaches the app's
    /// handler, as a throw would.
    /// </summary>
    public async void SaveCurrent() => await SaveCurrentAsync();

    /// <summary>Completes when the writes queued so far have run (or have failed).</summary>
    public Task FlushAsync() => _writes.FlushAsync();

    /// <summary>
    /// Waits for the writes still queued, as the app exits. They never need the UI thread, so waiting can't deadlock.
    /// </summary>
    public void Dispose() => _writes.FlushAsync().Wait();

    private async Task<StorageError?> TryWriteAsync(AppSettings snapshot)
    {
        try
        {
            await _writes.EnqueueAsync(() => SettingsStore.Save(snapshot));
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
