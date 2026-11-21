using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

/// <summary>
/// The settings changes the UI makes through async commands (theme, layout) still end up in the settings file, and the
/// startup path that has to wait for a settings write doesn't deadlock the UI thread. The file is the real settings
/// file, as in the other settings tests, and is put back when a test ends.
/// </summary>
public class SettingsCommandTests : IDisposable
{
    private readonly string? _originalText;

    public SettingsCommandTests()
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        _originalText = File.Exists(AppPaths.SettingsFile) ? File.ReadAllText(AppPaths.SettingsFile) : null;
    }

    [AvaloniaFact]
    public async Task The_layout_command_applies_the_layout_at_once_and_writes_it()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);
        var changes = 0;
        temp.Settings.Changed += () => changes++;

        var running = pane.SetLayoutCommand.ExecuteAsync(ResponseLayout.Horizontal);

        Assert.True(pane.IsSideBySide);
        Assert.Equal(1, changes);
        await running;
        Assert.Equal(ResponseLayout.Horizontal, SettingsStore.Load().Layout);

        await pane.SetLayoutCommand.ExecuteAsync(ResponseLayout.Vertical);

        Assert.False(pane.IsSideBySide);
        Assert.Equal(ResponseLayout.Vertical, SettingsStore.Load().Layout);
    }

    [AvaloniaFact]
    public async Task The_layout_buttons_stay_enabled_while_the_setting_is_written()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);

        // The command can't finish before this test yields: the write completes back on this (UI) thread.
        var running = pane.SetLayoutCommand.ExecuteAsync(ResponseLayout.Horizontal);

        Assert.True(pane.SetLayoutCommand.IsRunning);
        Assert.True(pane.SetLayoutCommand.CanExecute(ResponseLayout.Vertical));
        await running;
    }

    [AvaloniaFact]
    public async Task Toggling_the_theme_applies_it_and_writes_it()
    {
        using var temp = new TempWorkspace();

        await temp.Settings.ToggleThemeAsync();
        var first = temp.Settings.Current.Theme;
        Assert.Equal(first, SettingsStore.Load().Theme);

        await temp.Settings.ToggleThemeAsync();

        Assert.NotEqual(first, temp.Settings.Current.Theme);
        Assert.Equal(temp.Settings.Current.Theme, SettingsStore.Load().Theme);
    }

    [AvaloniaFact(Timeout = 30_000)]
    public void Updating_settings_while_waiting_on_the_UI_thread_does_not_deadlock()
    {
        using var temp = new TempWorkspace();
        var uiThread = Environment.CurrentManagedThreadId;
        var changedOn = -1;
        temp.Settings.Changed += () => changedOn = Environment.CurrentManagedThreadId;
        var settings = temp.Settings.Current.Copy();
        settings.Layout = ResponseLayout.Horizontal;
        settings.DataDirectory = Path.Combine(temp.Root, "chosen");

        var error = temp.Settings.Update(settings);

        Assert.Null(error);
        Assert.Same(settings, temp.Settings.Current);
        Assert.Equal(uiThread, changedOn);
        var written = SettingsStore.Load();
        Assert.Equal(ResponseLayout.Horizontal, written.Layout);
        Assert.Equal(settings.DataDirectory, written.DataDirectory);
    }

    [AvaloniaFact(Timeout = 30_000)]
    public void Updating_settings_that_cannot_be_written_returns_the_error_and_changes_nothing()
    {
        using var temp = new TempWorkspace();
        var before = temp.Settings.Current;
        File.Delete(AppPaths.SettingsFile);
        Directory.CreateDirectory(AppPaths.SettingsFile);
        try
        {
            var error = temp.Settings.Update(new AppSettings { Layout = ResponseLayout.Horizontal });

            Assert.NotNull(error);
            Assert.Same(before, temp.Settings.Current);
        }
        finally
        {
            Directory.Delete(AppPaths.SettingsFile, true);
        }
    }

    public void Dispose()
    {
        if (_originalText is not null)
            File.WriteAllText(AppPaths.SettingsFile, _originalText);
        else
            File.Delete(AppPaths.SettingsFile);
    }
}
