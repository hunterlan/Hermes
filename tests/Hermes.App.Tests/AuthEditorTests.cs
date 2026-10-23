using System.Net;
using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

public class AuthEditorTests
{
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [AvaloniaFact]
    public async Task Without_a_browser_the_sign_in_address_is_shown_to_copy()
    {
        using var oauth = new OAuth2Service();
        string? copied = null;
        var auth = new AuthEditorViewModel(oauth, _ => Task.FromResult(false), text => Task.FromResult(copied = text), allowInherit: false);
        auth.Load(new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2 =
            {
                GrantType = OAuth2GrantType.AuthorizationCode, AuthUrl = "https://auth.test/authorize", AccessTokenUrl = "https://auth.test/token",
                ClientId = "c", RedirectUri = $"http://localhost:{FreePort()}/callback",
            },
        });

        auth.TokenButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => auth.AuthorizationUrl is not null);
        Assert.StartsWith("https://auth.test/authorize?", auth.AuthorizationUrl);
        await auth.CopyAuthorizationUrlCommand.ExecuteAsync(null);
        Assert.Equal(auth.AuthorizationUrl, copied);

        auth.TokenButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => !auth.IsFetchingToken);
        Assert.Null(auth.AuthorizationUrl);
    }

    [AvaloniaFact]
    public void Token_requests_follow_the_request_SSL_setting()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        tab.VerifySslOverride = false;
        Assert.False(tab.Auth.VerifySslProvider());
        tab.VerifySslOverride = true;
        Assert.True(tab.Auth.VerifySslProvider());
        window.Close();
    }
}
