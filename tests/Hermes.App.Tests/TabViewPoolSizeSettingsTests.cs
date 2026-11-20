using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabViewPoolSizeSettingsTests : IAsyncDisposable
{
    // Resolved inside the test, once the headless app is running.
    private SettingsService _settings = null!;
    private AppSettings? _originalSettings;

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("11")]
    public void Invalid_pool_size_is_explained_and_blocks_save(string text)
    {
        var dialog = CreateDialog();
        var sizeBefore = _settings.Current.TabViewPoolSize;

        dialog.TabViewPoolSizeText = text;

        Assert.False(string.IsNullOrEmpty(dialog.TabViewPoolSizeError));
        Assert.False(dialog.SaveCommand.CanExecute(null));
        dialog.SaveCommand.Execute(null);
        Assert.Equal(sizeBefore, _settings.Current.TabViewPoolSize);
    }

    [AvaloniaTheory]
    [InlineData("1", 1)]
    [InlineData(" 5 ", 5)]
    [InlineData("10", 10)]
    public async Task Valid_pool_size_is_saved(string text, int expected)
    {
        var dialog = CreateDialog();

        dialog.TabViewPoolSizeText = text;

        Assert.Null(dialog.TabViewPoolSizeError);
        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Equal(expected, _settings.Current.TabViewPoolSize);
    }

    [AvaloniaFact]
    public async Task Saved_pool_size_applies_to_the_open_window()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var host = window.FindControl<TabViewHost>("TabViews")!;
        var dialog = CreateDialog();

        dialog.TabViewPoolSizeText = "7";
        await dialog.SaveCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal(7, vm.TabViewPoolSize);
        Assert.Equal(7, host.MaxLiveViews);
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_hand_edited_pool_size_is_kept_in_range()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        CreateDialog();

        await _settings.UpdateAsync(new AppSettings { TabViewPoolSize = 500 });
        Assert.Equal(SettingsDialogViewModel.MaxTabViewPoolSize, vm.TabViewPoolSize);
        await _settings.UpdateAsync(new AppSettings { TabViewPoolSize = -3 });
        Assert.Equal(1, vm.TabViewPoolSize);

        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        window.Close();
    }

    // Settings are shared, so the test puts them back, waiting until they are written and current again.
    public async ValueTask DisposeAsync()
    {
        if (_originalSettings is not null)
            await _settings.UpdateAsync(_originalSettings);
    }

    private SettingsDialogViewModel CreateDialog()
    {
        _settings = App.Services.GetRequiredService<SettingsService>();
        _originalSettings ??= _settings.Current;
        return new SettingsDialogViewModel(_settings, App.Services.GetRequiredService<DialogService>(),
            App.Services.GetRequiredService<HttpClientProvider>(), AppPaths.DefaultDataDirectory,
            () => Task.FromResult(true));
    }
}
