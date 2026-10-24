using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Auth;
using Hermes.Core.Models.Auth;
using Hermes.TestSupport;

namespace Hermes.App.Tests;

public class CancellationTests
{
    /// <summary>Answers after <paramref name="delay"/>, or never if the test is over first.</summary>
    private static LocalHttpServer SlowServer(TimeSpan delay) => new(async (ctx, stop) =>
    {
        await Task.Delay(delay, stop);
        await ctx.Response.OutputStream.WriteAsync("{\"access_token\":\"late\"}"u8.ToArray(), stop);
        ctx.Response.Close();
    });

    [AvaloniaFact]
    public async Task The_send_button_cancels_a_request_in_flight()
    {
        using var server = SlowServer(TimeSpan.FromMinutes(1));
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = server.Url;

        tab.SendButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => tab.IsSending);

        Assert.Same(tab.CancelSendCommand, tab.SendButtonCommand);
        Assert.True(tab.SendButtonCommand.CanExecute(null));
        tab.SendButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => !tab.IsSending);

        Assert.Same(tab.SendCommand, tab.SendButtonCommand);
        Assert.False(tab.Response.HasResponse);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_enter_sends_and_then_cancels()
    {
        using var server = SlowServer(TimeSpan.FromMinutes(1));
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = server.Url;

        vm.SendActiveCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => tab.IsSending);
        Assert.True(vm.SendActiveCommand.CanExecute(null));
        vm.SendActiveCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => !tab.IsSending);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_token_button_cancels_a_fetch_in_progress()
    {
        using var server = SlowServer(TimeSpan.FromMinutes(1));
        using var oauth = new OAuth2Service();
        var auth = new AuthEditorViewModel(oauth, _ => Task.FromResult(true), _ => Task.CompletedTask, allowInherit: false);
        auth.Load(new AuthConfig { Type = AuthType.OAuth2, OAuth2 = { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = server.Url, ClientId = "c" } });

        auth.TokenButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => auth.IsFetchingToken);

        Assert.Same(auth.CancelTokenCommand, auth.TokenButtonCommand);
        Assert.True(auth.TokenButtonCommand.CanExecute(null));
        auth.TokenButtonCommand.Execute(null);
        await UiTestHelpers.WaitUntil(() => !auth.IsFetchingToken);

        Assert.Equal("", auth.OAuthAccessToken);
        Assert.Same(auth.GetNewAccessTokenCommand, auth.TokenButtonCommand);
    }
}
