using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class DialogEscapeTests
{
    [AvaloniaFact]
    public void Escape_in_the_settings_dialog_cancels_it_once_without_saving()
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var originalSettings = settings.Current;
        var viewModel = new SettingsDialogViewModel(settings, App.Services.GetRequiredService<DialogService>(),
            App.Services.GetRequiredService<HttpClientProvider>(), AppPaths.DefaultDataDirectory,
            () => Task.FromResult(true));
        var closeRequests = new List<bool>();
        viewModel.CloseRequested += closeRequests.Add;
        var dialog = new SettingsDialog { DataContext = viewModel };
        var closedCount = 0;
        dialog.Closed += (_, _) => closedCount++;
        dialog.Show();
        UiTestHelpers.Pump();

        viewModel.TimeoutText = (originalSettings.RequestTimeoutMs + 1234).ToString();
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.Equal([false], closeRequests);
        Assert.Equal(1, closedCount);
        Assert.Equal(originalSettings, settings.Current);
    }

    [AvaloniaFact]
    public void Escape_in_the_code_snippet_dialog_closes_it()
    {
        var viewModel = new CodeSnippetViewModel("curl https://example.com", App.Services.GetRequiredService<DialogService>());
        var dialog = new CodeSnippetDialog { DataContext = viewModel };
        var closedCount = 0;
        dialog.Closed += (_, _) => closedCount++;
        dialog.Show();
        UiTestHelpers.Pump();

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.Equal(1, closedCount);
    }
}
