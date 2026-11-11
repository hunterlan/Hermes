using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Variables;
using Hermes.Core.Web;

namespace Hermes.Core.Auth;

/// <summary>
/// Fetches OAuth 2.0 tokens. Token requests use clients of their own, without cookies: cookies the identity
/// provider sets must not end up in the user's requests, and the user's cookies must not change how it answers.
/// </summary>
public sealed class OAuth2Service : IDisposable
{
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClientProvider _clients = new(useCookies: false);

    /// <param name="openBrowser">Opens the authorization URL in the system browser (authorization code flow only).</param>
    /// <param name="sslSource">Where <paramref name="verifySsl"/> comes from, so a certificate failure can say where to change it.</param>
    public async Task<OAuth2TokenResult> GetTokenAsync(OAuth2Auth config, VariableScope scope, Func<Uri, Task> openBrowser,
        bool verifySsl, CancellationToken cancellationToken, SslSettingSource sslSource = SslSettingSource.AppWide)
    {
        string R(string s) => VariableResolver.Resolve(s, scope).Trim();

        var tokenUrl = R(config.AccessTokenUrl);
        if (!Uri.TryCreate(tokenUrl, UriKind.Absolute, out var tokenUri))
            return Fail(OAuth2FailureDescriber.FromMessage("Access Token URL is missing or invalid."));

        var fields = new List<KeyValuePair<string, string>>();
        var scopeValue = R(config.Scope);
        switch (config.GrantType)
        {
            case OAuth2GrantType.ClientCredentials:
                fields.Add(new("grant_type", "client_credentials"));
                break;

            case OAuth2GrantType.Password:
                fields.Add(new("grant_type", "password"));
                fields.Add(new("username", R(config.Username)));
                fields.Add(new("password", R(config.Password)));
                break;

            case OAuth2GrantType.AuthorizationCode:
                var redirectUri = R(config.RedirectUri);
                var verifier = config.UsePkce ? CreateCodeVerifier() : null;
                var code = await AuthorizeAsync(config, R, redirectUri, verifier, openBrowser, cancellationToken).ConfigureAwait(false);
                if (code.Cancelled)
                    return new OAuth2TokenResult { IsCancelled = true };
                if (code.Failure is not null)
                    return Fail(code.Failure);
                fields.Add(new("grant_type", "authorization_code"));
                fields.Add(new("code", code.Code!));
                fields.Add(new("redirect_uri", redirectUri));
                if (verifier is not null)
                    fields.Add(new("code_verifier", verifier));
                // Scope is only sent with the authorization request in this flow.
                scopeValue = "";
                break;
        }

        if (scopeValue.Length > 0)
            fields.Add(new("scope", scopeValue));

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
        var clientId = R(config.ClientId);
        var clientSecret = R(config.ClientSecret);
        if (config.ClientAuthentication == OAuth2ClientAuthentication.Header && clientId.Length > 0)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
        else
        {
            fields.Add(new("client_id", clientId));
            if (clientSecret.Length > 0)
                fields.Add(new("client_secret", clientSecret));
        }
        request.Content = new FormUrlEncodedContent(fields);
        request.Headers.Accept.ParseAdd("application/json");

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _clients.Get(followRedirects: true, verifySsl).SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseTokenResponse(body, response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new OAuth2TokenResult { IsCancelled = true };
        }
        catch (OperationCanceledException)
        {
            return Fail(OAuth2FailureDescriber.FromTimeout(tokenUri));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
        {
            // A redirect rewrites the request's address, so the request holds the address that failed.
            var certificateFacts = _clients.CertificateFailures.Find(tokenUri.IdnHost, startTimestamp);
            return Fail(OAuth2FailureDescriber.FromNetworkFailure(ex, tokenUri, request.RequestUri ?? tokenUri, certificateFacts, sslSource, DateTime.Now));
        }
    }

    internal static OAuth2TokenResult ParseTokenResponse(string body, HttpStatusCode status)
    {
        // Repeated names keep the last value, so an odd response is reported as a failure, never thrown.
        var values = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Fail(OAuth2FailureDescriber.FromUnexpectedResponse("It answered, but not with a JSON object.", body, status));
            foreach (var p in doc.RootElement.EnumerateObject())
                values[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
        }
        catch (JsonException)
        {
            // Some providers (e.g. GitHub without an Accept header) answer form-encoded.
            foreach (var pair in UrlUtilities.ParseQuery(body))
                values[Uri.UnescapeDataString(pair.Key)] = Uri.UnescapeDataString(pair.Value.Replace('+', ' '));
        }

        if (values.TryGetValue("error", out var error))
        {
            values.TryGetValue("error_description", out var description);
            return Fail(OAuth2FailureDescriber.FromServerError(error, description, status));
        }
        if (!values.TryGetValue("access_token", out var token) || token.Length == 0)
            return Fail(OAuth2FailureDescriber.FromUnexpectedResponse("It answered, but the answer has no access token.", body, status));

        DateTimeOffset? expiresAt = values.TryGetValue("expires_in", out var expires) && int.TryParse(expires, out var seconds)
            ? DateTimeOffset.Now.AddSeconds(seconds)
            : null;
        return new OAuth2TokenResult
        {
            AccessToken = token,
            RefreshToken = values.GetValueOrDefault("refresh_token"),
            TokenType = values.GetValueOrDefault("token_type"),
            ExpiresAt = expiresAt,
        };
    }

    private readonly record struct CodeResult(string? Code, RequestFailure? Failure, bool Cancelled = false);

    private static async Task<CodeResult> AuthorizeAsync(OAuth2Auth config, Func<string, string> resolve, string redirectUri,
        string? verifier, Func<Uri, Task> openBrowser, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(resolve(config.AuthUrl), UriKind.Absolute, out var authUri))
            return new CodeResult(null, OAuth2FailureDescriber.FromMessage("Auth URL is missing or invalid."));
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != Uri.UriSchemeHttp || !redirect.IsLoopback)
            return new CodeResult(null, OAuth2FailureDescriber.FromMessage("Callback URL must be a loopback address such as http://localhost:5577/callback so Hermes can receive the authorization code."));

        var state = resolve(config.State);
        if (state.Length == 0)
            state = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        var query = new List<string>
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(resolve(config.ClientId)),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "state=" + Uri.EscapeDataString(state),
        };
        var scope = resolve(config.Scope);
        if (scope.Length > 0)
            query.Add("scope=" + Uri.EscapeDataString(scope));
        if (verifier is not null)
        {
            query.Add("code_challenge=" + CreateCodeChallenge(verifier));
            query.Add("code_challenge_method=S256");
        }
        var separator = authUri.Query.Length > 0 ? "&" : "?";
        var authorizationUrl = new Uri(authUri.AbsoluteUri + separator + string.Join('&', query));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirect.Scheme}://{redirect.Host}:{redirect.Port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            return new CodeResult(null, OAuth2FailureDescriber.FromCallbackListener(ex, redirect.Host, redirect.Port));
        }

        await openBrowser(authorizationUrl).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AuthorizationTimeout);
        await using var registration = timeout.Token.Register(listener.Stop);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                return cancellationToken.IsCancellationRequested
                    ? new CodeResult(null, null, Cancelled: true)
                    : new CodeResult(null, OAuth2FailureDescriber.FromMessage("Timed out waiting for authorization.", offerRetry: true));
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath.TrimEnd('/'), redirect.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var parameters = context.Request.QueryString;
            var result = parameters["error"] is { } error
                ? new CodeResult(null, OAuth2FailureDescriber.FromAuthorizationError(error, parameters["error_description"]))
                : parameters["state"] != state
                    ? new CodeResult(null, OAuth2FailureDescriber.FromMessage("State mismatch in the authorization response."))
                    : parameters["code"] is { Length: > 0 } code
                        ? new CodeResult(code, null)
                        : new CodeResult(null, OAuth2FailureDescriber.FromMessage("Authorization response did not contain a code."));

            var html = result.Failure is null
                ? "<html><body style=\"font-family:sans-serif\"><h2>Authorization complete</h2><p>You can close this window and return to Hermes.</p></body></html>"
                : $"<html><body style=\"font-family:sans-serif\"><h2>Authorization failed</h2><p>{WebUtility.HtmlEncode($"{result.Failure.Headline}. {result.Failure.Facts}")}</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            context.Response.Close();
            return result;
        }
    }

    internal static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string CreateCodeChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static OAuth2TokenResult Fail(RequestFailure failure) => new() { Failure = failure };

    public void Dispose() => _clients.Dispose();
}
