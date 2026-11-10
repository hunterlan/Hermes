using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Models.Auth;
using Hermes.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabDisposalTests
{
    [AvaloniaFact]
    public async Task Closing_a_tab_during_a_send_cancels_it_and_writes_nothing()
    {
        using var server = new LocalHttpServer(async (ctx, stop) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
            ctx.Response.Close();
        });
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var workspace = App.Services.GetRequiredService<Services.WorkspaceService>();
        var historyBefore = workspace.History.Count;
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = server.Url;

        tab.SendButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => tab.IsSending);
        vm.RemoveTab(tab);
        await UiTestHelpers.WaitUntil(() => !tab.IsSending);

        Assert.False(tab.Response.HasResponse);
        Assert.Equal(historyBefore, workspace.History.Count);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Closing_a_tab_during_authorization_frees_the_callback_port()
    {
        var port = FreePort();
        using var oauth = new OAuth2Service();
        var auth = new AuthEditorViewModel(oauth, _ => Task.FromResult(true), _ => Task.CompletedTask, allowInherit: false);
        auth.Load(new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2 =
            {
                GrantType = OAuth2GrantType.AuthorizationCode,
                AuthUrl = "http://localhost/authorize",
                AccessTokenUrl = "http://localhost/token",
                RedirectUri = $"http://localhost:{port}/callback",
                ClientId = "c",
            },
        });

        auth.TokenButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => auth.IsFetchingToken);
        Assert.Throws<HttpListenerException>(() => Listen(port));

        auth.Dispose();
        await UiTestHelpers.WaitUntil(() => !auth.IsFetchingToken);

        using var listener = Listen(port);
        Assert.True(listener.IsListening);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static HttpListener Listen(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        return listener;
    }
}
