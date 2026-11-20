using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.Core.Serialization;
using Hermes.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>
/// The Send button on its own: what needs a yes before a send, and which request a response is kept with. Cancelling,
/// closing the tab during a send, Save As during a send and showing the response before recording it are covered
/// through the tab in <see cref="RequestTabSendTests"/>, <see cref="CancellationTests"/> and
/// <see cref="TabDisposalTests"/>.
/// </summary>
public class RequestSendViewModelTests
{
    /// <summary>A saved request whose id the test can change, the way Save As does during a send.</summary>
    private sealed class SavedRequest(WorkspaceService workspace, HttpRequestModel model) : IRequestSource
    {
        public Guid RequestId { get; set; } = model.Id;
        public bool IsPersisted => workspace.Find(RequestId) is not null;
        public HttpRequestModel BuildModel() => ModelCloner.Clone(model);
        public RequestContext CreateContext() => workspace.CreateContext(RequestId);
    }

    private sealed record SendFixture(Window Window, SavedRequest Request, ResponseViewModel Response, RequestSendViewModel Sending);

    private static async Task<SendFixture> OpenAsync(TempWorkspace temp, string url)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var dialogs = new DialogService { Owner = window };
        var services = new TabServices(temp.Workspace,
            new RequestSendService(temp.Workspace, App.Services.GetRequiredService<IRequestExecutor>()),
            new RequestSaveService(temp.Workspace, dialogs), App.Services.GetRequiredService<OAuth2Service>(), dialogs,
            temp.Settings, App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var model = new HttpRequestModel { Name = "List", Url = url };
        await temp.Workspace.AddNodeAsync(collection, null, model);
        var request = new SavedRequest(temp.Workspace, model);
        var response = new ResponseViewModel(dialogs, temp.Settings);
        return new SendFixture(window, request, response, new RequestSendViewModel(request, services, response));
    }

    private static async Task<EnvironmentModel> ActivateEnvironmentAskingBeforeSendingAsync(TempWorkspace temp)
    {
        var environment = await temp.Workspace.CreateEnvironmentAsync("Production");
        environment.ConfirmBeforeSend = true;
        temp.Workspace.ActiveEnvironment = environment;
        return environment;
    }

    /// <summary>Waits for the next confirmation over <paramref name="window"/> and returns it.</summary>
    private static async Task<MessageDialog> NextDialog(Window window)
    {
        await UiTestHelpers.WaitUntil(() => window.OwnedWindows.OfType<MessageDialog>().Any());
        return window.OwnedWindows.OfType<MessageDialog>().Single();
    }

    /// <summary>Answers <paramref name="dialog"/> and waits until it is gone.</summary>
    private static async Task Answer(Window window, MessageDialog dialog, string answer)
    {
        dialog.Close(answer);
        await UiTestHelpers.WaitUntil(() => !window.OwnedWindows.Contains(dialog));
    }

    [AvaloniaFact]
    public async Task Unresolved_variables_are_confirmed_before_the_environment_and_both_yeses_send()
    {
        using var temp = new TempWorkspace();
        using var server = new JsonServer();
        var (window, request, response, sending) = await OpenAsync(temp, server.Url + "?q={{missing}}");
        var environment = await ActivateEnvironmentAskingBeforeSendingAsync(temp);

        var send = sending.SendCommand.ExecuteAsync(null);
        var unresolved = await NextDialog(window);
        Assert.Equal("Unresolved variables", unresolved.Title);
        Assert.False(sending.IsSending);
        await Answer(window, unresolved, "confirm");

        var askBeforeSending = await NextDialog(window);
        Assert.Equal($"Send to {environment.Name}?", askBeforeSending.Title);
        Assert.False(sending.IsSending);
        await Answer(window, askBeforeSending, "confirm");

        await UiTestHelpers.WaitUntil(() => send.IsCompleted);
        await send;
        Assert.True(response.HasResponse);
        Assert.Single(temp.Workspace.History);
        Assert.Single(temp.Workspace.LoadResponses(request.RequestId));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Declining_the_unresolved_variables_sends_nothing_and_asks_nothing_else()
    {
        using var temp = new TempWorkspace();
        using var server = new JsonServer();
        var (window, _, response, sending) = await OpenAsync(temp, server.Url + "?q={{missing}}");
        await ActivateEnvironmentAskingBeforeSendingAsync(temp);

        var send = sending.SendCommand.ExecuteAsync(null);
        await Answer(window, await NextDialog(window), "cancel");
        await UiTestHelpers.WaitUntil(() => send.IsCompleted);
        await send;

        Assert.Empty(window.OwnedWindows);
        Assert.False(response.HasResponse);
        Assert.False(response.IsLoading);
        Assert.Empty(temp.Workspace.History);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_tab_closed_while_a_confirmation_is_open_sends_nothing_after_the_yes()
    {
        using var temp = new TempWorkspace();
        using var server = new JsonServer();
        var (window, _, response, sending) = await OpenAsync(temp, server.Url);
        await ActivateEnvironmentAskingBeforeSendingAsync(temp);

        var send = sending.SendCommand.ExecuteAsync(null);
        var askBeforeSending = await NextDialog(window);
        sending.Dispose();
        await Answer(window, askBeforeSending, "confirm");
        await UiTestHelpers.WaitUntil(() => send.IsCompleted);
        await send;

        Assert.False(sending.IsSending);
        Assert.False(response.IsLoading);
        Assert.False(response.HasResponse);
        Assert.Empty(temp.Workspace.History);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_response_is_kept_with_the_request_as_it_is_when_the_send_ends()
    {
        using var temp = new TempWorkspace();
        var answer = new TaskCompletionSource();
        using var server = new LocalHttpServer(async (ctx, stop) =>
        {
            await answer.Task.WaitAsync(stop);
            await ctx.Response.OutputStream.WriteAsync("{\"ok\":true}"u8.ToArray(), stop);
            ctx.Response.Close();
        });
        var (window, request, response, sending) = await OpenAsync(temp, server.Url);
        var originalId = request.RequestId;
        var savedAs = new HttpRequestModel { Name = "List copy", Url = server.Url };
        await temp.Workspace.AddNodeAsync(await temp.Workspace.CreateCollectionAsync("Copies"), null, savedAs);

        var send = sending.SendCommand.ExecuteAsync(null);
        await UiTestHelpers.WaitUntil(() => sending.IsSending);
        request.RequestId = savedAs.Id;
        answer.SetResult();
        await UiTestHelpers.WaitUntil(() => send.IsCompleted);
        await send;

        Assert.True(response.HasResponse);
        Assert.Single(temp.Workspace.LoadResponses(savedAs.Id));
        Assert.Empty(temp.Workspace.LoadResponses(originalId));
        Assert.Single(response.Saved.Items);
        window.Close();
    }
}
