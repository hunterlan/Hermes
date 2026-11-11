using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>
/// Settings that can't be written. The settings file is made unwritable by putting a folder where it belongs, which
/// works the same on every platform (and as root); it is put back when the test ends.
/// </summary>
public class SettingsWriteFailureTests : IDisposable
{
    private readonly SettingsService _settings = new();
    private readonly AppSettings _original;
    private readonly string? _originalText;

    public SettingsWriteFailureTests()
    {
        _original = _settings.Current;
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        _originalText = File.Exists(AppPaths.SettingsFile) ? File.ReadAllText(AppPaths.SettingsFile) : null;
        File.Delete(AppPaths.SettingsFile);
        Directory.CreateDirectory(AppPaths.SettingsFile);
    }

    private void RestoreWritableFile() => Directory.Delete(AppPaths.SettingsFile, true);

    [AvaloniaFact]
    public void Update_returns_the_error_and_changes_nothing()
    {
        var settings = new AppSettings { Layout = ResponseLayout.Horizontal };
        var changes = 0;
        _settings.Changed += () => changes++;

        var error = _settings.Update(settings);

        Assert.NotNull(error);
        Assert.StartsWith("Settings couldn't be saved", error.Summary);
        Assert.Equal(AppPaths.SettingsFile, error.Location);
        Assert.Same(_original, _settings.Current);
        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void An_in_place_change_that_cannot_be_written_is_reported_once()
    {
        var problems = new List<StorageError>();
        _settings.StorageProblem += problems.Add;

        _settings.Current.Layout = ResponseLayout.Horizontal;
        _settings.SaveCurrent();
        _settings.SaveCurrent();
        _settings.SaveCurrent();

        Assert.Single(problems);
        Assert.Equal(ResponseLayout.Horizontal, _settings.Current.Layout);

        // Once a write works the next failure is news again.
        RestoreWritableFile();
        _settings.SaveCurrent();
        File.Delete(AppPaths.SettingsFile);
        Directory.CreateDirectory(AppPaths.SettingsFile);
        _settings.SaveCurrent();
        Assert.Equal(2, problems.Count);
    }

    [AvaloniaFact]
    public async Task The_settings_dialog_shows_the_problem_and_stays_open()
    {
        var closed = false;
        var dialog = new SettingsDialogViewModel(_settings, App.Services.GetRequiredService<DialogService>(),
            App.Services.GetRequiredService<HttpClientProvider>(), AppPaths.DefaultDataDirectory, () => Task.FromResult(true));
        dialog.CloseRequested += _ => closed = true;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.StartsWith("Settings couldn't be saved", dialog.SaveError);

        RestoreWritableFile();
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.Null(dialog.SaveError);
    }

    public void Dispose()
    {
        if (Directory.Exists(AppPaths.SettingsFile))
            Directory.Delete(AppPaths.SettingsFile, true);
        if (_originalText is not null)
            File.WriteAllText(AppPaths.SettingsFile, _originalText);
        else
            File.Delete(AppPaths.SettingsFile);
    }
}
