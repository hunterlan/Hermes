using System.Net;
using Hermes.Core.Auth;
using Hermes.Core.Formatting;
using Hermes.Core.Http;
using Hermes.Core.Models.Auth;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

public class FormatterAndOAuthTests
{
    [Fact]
    public void Formats_valid_json()
    {
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ]\n}", JsonFormatter.Format("{\"a\":[1,2]}").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Formats_json_with_unquoted_variables()
    {
        var formatted = JsonFormatter.Format("{\"id\":{{id}},\"tags\":[]}");
        Assert.Equal("{\n  \"id\": {{id}},\n  \"tags\": []\n}", formatted);
    }

    [Fact]
    public void Strict_json_formatting_leaves_text_that_is_not_json()
    {
        Assert.True(JsonFormatter.TryFormat("[1]", out var formatted));
        Assert.Equal("[\n  1\n]", formatted.ReplaceLineEndings("\n"));
        Assert.False(JsonFormatter.TryFormat("{\"id\":{{id}}}", out formatted));
        Assert.Equal("{\"id\":{{id}}}", formatted);
        // A response's comments aren't dropped from its pretty view.
        Assert.False(JsonFormatter.TryFormat("[1 /* one */]", out _));
    }

    [Fact]
    public void Formats_xml()
    {
        Assert.True(XmlFormatter.TryFormat("<a><b>1</b></a>", out var xml));
        Assert.Contains("\n  <b>1</b>", xml.ReplaceLineEndings("\n"));
        Assert.False(XmlFormatter.TryFormat("<a>", out _));
    }

    [Fact]
    public void Pkce_challenge_matches_rfc7636_example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            OAuth2Service.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Parses_token_responses()
    {
        var ok = OAuth2Service.ParseTokenResponse("{\"access_token\":\"t\",\"expires_in\":3600,\"refresh_token\":\"r\"}", HttpStatusCode.OK);
        Assert.Equal("t", ok.AccessToken);
        Assert.Equal("r", ok.RefreshToken);
        Assert.NotNull(ok.ExpiresAt);

        var form = OAuth2Service.ParseTokenResponse("access_token=abc&token_type=bearer", HttpStatusCode.OK);
        Assert.Equal("abc", form.AccessToken);

        var error = OAuth2Service.ParseTokenResponse("{\"error\":\"invalid_client\",\"error_description\":\"bad\"}", HttpStatusCode.BadRequest);
        Assert.Equal("invalid_client: bad", error.Error);
    }

    [Fact]
    public void Odd_token_responses_are_failures_not_exceptions()
    {
        Assert.NotNull(OAuth2Service.ParseTokenResponse("[1, 2]", HttpStatusCode.OK).Error);
        Assert.Equal("second", OAuth2Service.ParseTokenResponse("{\"access_token\":\"first\",\"access_token\":\"second\"}", HttpStatusCode.OK).AccessToken);
        Assert.Equal("b", OAuth2Service.ParseTokenResponse("access_token=a&access_token=b", HttpStatusCode.OK).AccessToken);
    }

    [Fact]
    public async Task Token_requests_neither_send_nor_keep_the_users_cookies()
    {
        string? cookieSent = null;
        using var server = new LocalHttpServer(async (ctx, stop) =>
        {
            cookieSent = ctx.Request.Headers["Cookie"];
            ctx.Response.AppendHeader("Set-Cookie", "idp_session=1; Path=/");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync("{\"access_token\":\"t\"}"u8.ToArray(), stop);
            ctx.Response.Close();
        });
        using var userClients = new HttpClientProvider();
        userClients.Cookies.Add(new Uri(server.Url), new Cookie("user_session", "1"));
        using var oauth = new OAuth2Service();

        var result = await oauth.GetTokenAsync(
            new OAuth2Auth { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = server.Url + "token", ClientId = "c" },
            Variables.VariableScope.Empty, _ => Task.CompletedTask, verifySsl: true, TestContext.Current.CancellationToken);

        Assert.Equal("t", result.AccessToken);
        Assert.Null(cookieSent);
        Assert.DoesNotContain(userClients.GetAllCookies(), c => c.Name == "idp_session");
    }

    [Fact]
    public async Task Token_endpoint_connection_failure_is_reported()
    {
        using var port = new ResettingPort();
        using var oauth = new OAuth2Service();

        var result = await oauth.GetTokenAsync(
            new OAuth2Auth { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = $"http://127.0.0.1:{port.Port}/token", ClientId = "c" },
            Variables.VariableScope.Empty, _ => Task.CompletedTask, verifySsl: true, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Error);
        Assert.Null(result.AccessToken);
    }
}
