using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class HistoryRetentionSettingsTests : IDisposable
{
    // Resolved inside the test, once the headless app is running.
    private SettingsService _settings = null!;
    private AppSettings? _originalSettings;

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("99999999999")]
    [InlineData("36501")]
    [InlineData("1000000")]
    [InlineData("2147483647")]
    public void Invalid_retention_is_explained_and_blocks_save(string text)
    {
        var dialog = CreateDialog();
        var closed = false;
        dialog.CloseRequested += _ => closed = true;

        dialog.HistoryRetentionText = text;
        var daysBefore = _settings.Current.HistoryRetentionDays;

        Assert.False(string.IsNullOrEmpty(dialog.HistoryRetentionError));
        Assert.False(dialog.SaveCommand.CanExecute(null));
        dialog.SaveCommand.Execute(null);
        Assert.False(closed);
        Assert.Equal(daysBefore, _settings.Current.HistoryRetentionDays);
    }

    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData(" 30 ", 30)]
    [InlineData("36500", 36500)]
    public void Valid_retention_is_saved(string text, int expectedDays)
    {
        var dialog = CreateDialog();

        dialog.HistoryRetentionText = text;

        Assert.Null(dialog.HistoryRetentionError);
        Assert.True(dialog.SaveCommand.CanExecute(null));
        dialog.SaveCommand.Execute(null);
        Assert.Equal(expectedDays, _settings.Current.HistoryRetentionDays);
    }

    [AvaloniaFact]
    public void Fixing_the_value_clears_the_error_and_enables_save()
    {
        var dialog = CreateDialog();
        var canExecuteChanges = 0;
        dialog.SaveCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;

        dialog.HistoryRetentionText = "-5";
        Assert.NotNull(dialog.HistoryRetentionError);
        dialog.HistoryRetentionText = "5";

        Assert.Null(dialog.HistoryRetentionError);
        Assert.True(dialog.SaveCommand.CanExecute(null));
        Assert.True(canExecuteChanges >= 2);
    }

    public void Dispose()
    {
        if (_originalSettings is not null)
            _settings.Update(_originalSettings);
    }

    private SettingsDialogViewModel CreateDialog()
    {
        _settings = App.Services.GetRequiredService<SettingsService>();
        _originalSettings = _settings.Current;
        return new SettingsDialogViewModel(_settings, App.Services.GetRequiredService<DialogService>(),
            App.Services.GetRequiredService<HttpClientProvider>(), AppPaths.DefaultDataDirectory,
            () => Task.FromResult(true));
    }
}
