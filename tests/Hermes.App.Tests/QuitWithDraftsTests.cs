using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>Unsaved request edits live only in the session, so quitting asks first when the session can't be written.</summary>
public class QuitWithDraftsTests
{
    private sealed record MainWindowFixture(MainWindowViewModel ViewModel, Window Window);

    private static MainWindowFixture Create(TempWorkspace temp)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var sender = new RequestSendService(temp.Workspace, App.Services.GetRequiredService<IRequestExecutor>());
        var services = new TabServices(temp.Workspace, sender,
            App.Services.GetRequiredService<OAuth2Service>(), new DialogService { Owner = window }, temp.Settings,
            App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
        return new MainWindowFixture(new MainWindowViewModel(services), window);
    }

    private static RequestTabViewModel OpenEditedRequest(MainWindowViewModel viewModel)
    {
        viewModel.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        var tab = Assert.IsType<RequestTabViewModel>(viewModel.ActiveTab);
        tab.Url = "https://example.com/draft";
        Assert.True(tab.IsDirty);
        Assert.True(tab.KeepsDraftInSession);
        return tab;
    }

    [AvaloniaFact]
    public void A_draft_that_cant_be_kept_stops_the_quit()
    {
        using var temp = new TempWorkspace();
        var (viewModel, window) = Create(temp);
        OpenEditedRequest(viewModel);
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var error = viewModel.SaveSessionBeforeQuit();

        Assert.NotNull(error);
        Assert.Contains("Session", error.Summary);
        viewModel.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public void Without_drafts_a_session_that_cant_be_written_doesnt_stop_the_quit()
    {
        using var temp = new TempWorkspace();
        var (viewModel, window) = Create(temp);
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        Assert.Null(viewModel.SaveSessionBeforeQuit());
        viewModel.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public void A_draft_is_written_to_the_session_when_it_can_be()
    {
        using var temp = new TempWorkspace();
        var (viewModel, window) = Create(temp);
        OpenEditedRequest(viewModel);

        Assert.Null(viewModel.SaveSessionBeforeQuit());

        Assert.Contains("https://example.com/draft", File.ReadAllText(temp.Repository.SessionFile));
        viewModel.Dispose();
        window.Close();
    }
}
