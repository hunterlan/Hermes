using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

public sealed class RequestExecutor(HttpClientProvider clients)
{
    public async Task<ExecutionResult> ExecuteAsync(PreparedRequest request, CancellationToken cancellationToken)
    {
        HttpRequestMessage message;
        try
        {
            message = HttpMessageFactory.Create(request);
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return new ExecutionResult { Error = ex.Message, ResolvedUrl = request.Url };
        }

        using var _ = message;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.TimeoutMs > 0)
            timeout.CancelAfter(request.TimeoutMs);

        var client = clients.Get(request.FollowRedirects, request.VerifySsl);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var ttfb = stopwatch.Elapsed;
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            var total = stopwatch.Elapsed;

            var finalUri = response.RequestMessage?.RequestUri ?? message.RequestUri;
            return new ExecutionResult
            {
                ResolvedUrl = request.Url,
                Response = new ResponseModel
                {
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    HttpVersion = $"HTTP/{response.Version}",
                    Headers = CollectHeaders(response),
                    HeadersSize = EstimateHeadersSize(response),
                    Body = body,
                    ContentType = response.Content.Headers.ContentType?.MediaType,
                    Charset = response.Content.Headers.ContentType?.CharSet,
                    FinalUri = finalUri,
                    TimeToFirstByte = ttfb,
                    TotalTime = total,
                    Cookies = finalUri is null ? [] : CollectCookies(finalUri),
                },
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult { Cancelled = true, ResolvedUrl = request.Url };
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult { Error = $"Request timed out after {request.TimeoutMs} ms.", ResolvedUrl = request.Url };
        }
        catch (HttpRequestException ex)
        {
            return new ExecutionResult { Error = Describe(ex), ResolvedUrl = request.Url };
        }
        catch (IOException ex)
        {
            return new ExecutionResult { Error = ex.Message, ResolvedUrl = request.Url };
        }
    }

    private static List<KeyValueItem> CollectHeaders(HttpResponseMessage response)
    {
        var list = new List<KeyValueItem>();
        foreach (var h in response.Headers)
            foreach (var v in h.Value)
                list.Add(new KeyValueItem(h.Key, v));
        foreach (var h in response.Content.Headers)
            foreach (var v in h.Value)
                list.Add(new KeyValueItem(h.Key, v));
        return list;
    }

    private static long EstimateHeadersSize(HttpResponseMessage response)
    {
        // "HTTP/1.1 200 OK\r\n" + "Name: value\r\n" per header + "\r\n"
        long size = 9 + 4 + (response.ReasonPhrase?.Length ?? 0) + 2 + 2;
        foreach (var h in response.Headers.Concat(response.Content.Headers))
            foreach (var v in h.Value)
                size += h.Key.Length + 2 + v.Length + 2;
        return size;
    }

    private List<ResponseCookie> CollectCookies(Uri uri) =>
        clients.Cookies.GetCookies(uri).Select(c => new ResponseCookie
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain,
            Path = c.Path,
            Expires = c.Expires == DateTime.MinValue ? null : c.Expires,
            HttpOnly = c.HttpOnly,
            Secure = c.Secure,
        }).ToList();

    private static string Describe(HttpRequestException ex)
    {
        var inner = ex.InnerException;
        if (inner is AuthenticationException)
            return $"SSL error: {inner.Message} You can disable SSL certificate verification in the request settings.";
        return inner is null || inner.Message == ex.Message ? ex.Message : $"{ex.Message} ({inner.Message})";
    }
}
