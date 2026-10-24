using Hermes.Core.Auth;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Secrets;
using Hermes.Core.Variables;

namespace Hermes.Core.Http;

public static class RequestPreparer
{
    public const string UserAgent = "HermesRuntime/1.0";

    /// <summary>Headers added automatically unless the user sets them. Shown as "hidden headers" in the UI.</summary>
    public static IReadOnlyList<KeyValueItem> DefaultHeaders { get; } =
    [
        new("User-Agent", UserAgent),
        new("Accept", "*/*"),
        new("Accept-Encoding", "gzip, deflate, br"),
        new("Connection", "keep-alive"),
    ];

    public static PreparedRequest Prepare(HttpRequestModel request, RequestContext context, bool includeDefaultHeaders = true) =>
        Prepare(request, context, context.BuildScope(), context.ResolveAuth(request.Auth).Auth, includeDefaultHeaders);

    /// <summary>
    /// The request to send, and a copy to show or save in which secret variables and auth credentials are masked.
    /// Both come from the same context and the same dynamic values, so the copy matches what was sent except for
    /// the masked parts. (A secret whose value itself uses dynamic variables may shift later dynamic values in the
    /// copy; the secret is masked either way.)
    /// </summary>
    public static (PreparedRequest Request, PreparedRequest Masked) PrepareWithMasked(HttpRequestModel request, RequestContext context)
    {
        var dynamicValues = new DynamicValueLog();
        var auth = context.ResolveAuth(request.Auth).Auth;
        var prepared = Prepare(request, context, context.BuildScope().UseDynamicValues(dynamicValues), auth, includeDefaultHeaders: true);
        var masked = Prepare(request, context, context.BuildScope().MaskSecrets().UseDynamicValues(dynamicValues.Replay()),
            SecretFields.Masked(auth), includeDefaultHeaders: true);

        // Secret values that reached the request some other way (a copy typed in by hand) are masked by value.
        var scope = context.BuildScope();
        var secretValues = scope.Values.Where(v => scope.IsSecret(v.Key)).Select(v => v.Value)
            .Concat(SecretFields.Values(auth).Select(v => VariableResolver.Resolve(v, scope)));
        SecretMasker.MaskOccurrences(masked, secretValues);
        return (prepared, masked);
    }

    private static PreparedRequest Prepare(HttpRequestModel request, RequestContext context, VariableScope scope, AuthConfig auth,
        bool includeDefaultHeaders)
    {
        string R(string s) => VariableResolver.Resolve(s, scope);

        var prepared = new PreparedRequest
        {
            Method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : R(request.Method).Trim().ToUpperInvariant(),
            BodyMode = request.Body.Mode,
            FollowRedirects = request.Settings.FollowRedirects ?? context.Settings.FollowRedirects,
            VerifySsl = request.Settings.VerifySsl ?? context.Settings.VerifySsl,
            TimeoutMs = request.Settings.TimeoutMs ?? context.Settings.RequestTimeoutMs,
        };

        // URL: path variables first (their values may contain variables too), then {{vars}}. A repeated name
        // (possible in imported collections) takes its last value, as in Postman.
        var pathValues = new Dictionary<string, string>();
        foreach (var p in request.PathVariables.Where(p => p.Key.Length > 0))
            pathValues[p.Key] = R(p.Value);
        prepared.Url = UrlUtilities.EnsureScheme(R(UrlUtilities.ReplacePathVariables(request.Url, pathValues)));

        foreach (var h in request.Headers)
            if (h.Enabled && h.Key.Trim().Length > 0)
                prepared.Headers.Add(new KeyValueItem(R(h.Key).Trim(), R(h.Value)));

        AuthApplier.Apply(auth, scope, prepared);

        switch (request.Body.Mode)
        {
            case BodyMode.Raw:
                prepared.RawBody = R(request.Body.Raw);
                AddIfMissing(prepared, "Content-Type", RequestBody.ContentTypeFor(request.Body.RawLanguage));
                break;
            case BodyMode.UrlEncoded:
                prepared.FormFields.AddRange(ResolveFields(request.Body.UrlEncoded, R));
                AddIfMissing(prepared, "Content-Type", "application/x-www-form-urlencoded");
                break;
            case BodyMode.FormData:
                prepared.FormFields.AddRange(ResolveFields(request.Body.FormData, R));
                // The multipart boundary is generated when sending, so the user's Content-Type can't be honoured.
                prepared.Headers.RemoveAll(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
                break;
            case BodyMode.Binary:
                prepared.BinaryFilePath = request.Body.BinaryFilePath is { } path ? R(path) : null;
                break;
        }

        if (includeDefaultHeaders)
            foreach (var h in DefaultHeaders)
                AddIfMissing(prepared, h.Key, h.Value);

        return prepared;
    }

    private static IEnumerable<KeyValueItem> ResolveFields(IEnumerable<KeyValueItem> items, Func<string, string> resolve) =>
        items.Where(i => i.Enabled && i.Key.Length > 0)
             .Select(i => new KeyValueItem(resolve(i.Key), resolve(i.Value)) { Kind = i.Kind });

    private static void AddIfMissing(PreparedRequest request, string name, string value)
    {
        if (request.GetHeader(name) is null)
            request.Headers.Add(new KeyValueItem(name, value));
    }
}
