using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>What a tab shows and records when a send ends: the response first, kept with the request the tab is by then.</summary>
public class RequestTabSendTests
{
    private sealed record TabFixture(RequestTabViewModel Tab, HttpRequestModel Request, Window Window);

    private static TabFixture OpenSavedRequest(TempWorkspace temp, string url)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var sender = new RequestSendService(temp.Workspace, App.Services.GetRequiredService<IRequestExecutor>());
        var dialogs = new DialogService { Owner = window };
        var services = new TabServices(temp.Workspace, sender, new RequestSaveService(temp.Workspace, dialogs),
            App.Services.GetRequiredService<OAuth2Service>(), dialogs, temp.Settings,
            App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
        var collection = temp.Workspace.CreateCollection("Pets");
        var request = new HttpRequestModel { Name = "List", Url = url };
        temp.Workspace.AddNode(collection, null, request);
        return new TabFixture(new RequestTabViewModel(services, request), request, window);
    }

    /// <summary>Answers once <paramref name="answer"/> is completed, or never if the test is over first.</summary>
    private static LocalHttpServer ServerAnsweringWhen(Task answer) => new(async (ctx, stop) =>
    {
        await answer.WaitAsync(stop);
        await ctx.Response.OutputStream.WriteAsync("{\"ok\":true}"u8.ToArray(), stop);
        ctx.Response.Close();
    });

    [AvaloniaFact]
    public async Task Save_as_during_a_send_keeps_the_response_with_the_new_request()
    {
        using var temp = new TempWorkspace();
        var answer = new TaskCompletionSource();
        using var server = ServerAnsweringWhen(answer.Task);
        var (tab, original, window) = OpenSavedRequest(temp, server.Url);

        var sending = tab.Sending.SendCommand.ExecuteAsync(null);
        await UiTestHelpers.WaitUntil(() => tab.Sending.IsSending);
        var savingAs = tab.SaveAsCommand.ExecuteAsync(null);
        await UiTestHelpers.WaitUntil(() => window.OwnedWindows.OfType<SaveRequestDialog>().Any());
        var dialog = window.OwnedWindows.OfType<SaveRequestDialog>().Single();
        var choice = Assert.IsType<SaveRequestDialogViewModel>(dialog.DataContext);
        choice.Name = "List copy";
        choice.SaveCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => savingAs.IsCompleted);
        Assert.NotEqual(original.Id, tab.RequestId);

        answer.SetResult();
        await UiTestHelpers.WaitUntil(() => sending.IsCompleted);
        await sending;

        Assert.True(tab.Response.HasResponse);
        Assert.Single(temp.Workspace.LoadResponses(tab.RequestId));
        Assert.Single(tab.Response.SavedResponses);
        Assert.Empty(temp.Workspace.LoadResponses(original.Id));
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_response_is_shown_even_if_recording_it_fails()
    {
        using var temp = new TempWorkspace();
        using var server = ServerAnsweringWhen(Task.CompletedTask);
        var (tab, _, window) = OpenSavedRequest(temp, server.Url);
        temp.Workspace.History.CollectionChanged += (_, _) => throw new InvalidOperationException("History is broken");

        await Assert.ThrowsAsync<InvalidOperationException>(() => tab.Sending.SendCommand.ExecuteAsync(null));

        Assert.True(tab.Response.HasResponse);
        Assert.False(tab.Response.IsLoading);
        Assert.False(tab.Sending.IsSending);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_cancelled_send_shows_no_response_and_records_nothing()
    {
        using var temp = new TempWorkspace();
        var answer = new TaskCompletionSource();
        using var server = ServerAnsweringWhen(answer.Task);
        var (tab, request, window) = OpenSavedRequest(temp, server.Url);

        var sending = tab.Sending.SendCommand.ExecuteAsync(null);
        await UiTestHelpers.WaitUntil(() => tab.Sending.IsSending);
        tab.Sending.CancelSendCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => sending.IsCompleted);
        await sending;

        Assert.False(tab.Response.HasResponse);
        Assert.False(tab.Response.IsLoading);
        Assert.Empty(temp.Workspace.History);
        Assert.Empty(temp.Workspace.LoadResponses(request.Id));
        window.Close();
    }
}
