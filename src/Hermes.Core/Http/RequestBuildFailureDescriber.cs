using System.Text.RegularExpressions;
using Hermes.Core.Headers;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>Turns a failure to build the request from the prepared one (bad URL, unreadable body file, bad header) into a <see cref="RequestFailure"/>.</summary>
public static partial class RequestBuildFailureDescriber
{
    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex VariableReference();

    /// <summary>Always a failure: one the request's own fields explain, or, for anything else, the message as it is.</summary>
    public static RequestFailure Describe(Exception exception, PreparedRequest request) => exception switch
    {
        UriFormatException => DescribeInvalidUrl(exception, request.Url),
        BodyFileException bodyFile => DescribeBodyFile(bodyFile),
        InvalidHeaderException header => DescribeInvalidHeader(header),
        _ => RequestFailure.FromMessage(ExceptionText.Flatten(exception)),
    };

    private static RequestFailure DescribeInvalidUrl(Exception exception, string url) => new()
    {
        Kind = RequestFailureKind.InvalidUrl,
        Headline = "URL isn't valid",
        Facts = FindUrlProblem(url),
        NothingWasSent = true,
        Details = url.Length == 0 ? [] : [new FailureDetail("URL", url)],
        OriginalMessage = ExceptionText.Flatten(exception),
        Actions = [RequestFailureAction.EditUrl],
    };

    /// <summary>What is wrong with the URL, found by looking at the URL itself.</summary>
    private static string FindUrlProblem(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0)
            return "The URL is empty.";

        var variable = VariableReference().Match(trimmed);
        if (variable.Success)
            return $"The URL still has a variable that isn't resolved: {variable.Value}. Set it in the active environment.";

        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return "The URL doesn't start with http:// or https://.";

        var scheme = trimmed[..schemeEnd];
        if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return $"The scheme {scheme}:// isn't supported. Use http:// or https://.";

        var afterScheme = trimmed[(schemeEnd + 3)..];
        if (afterScheme.Length == 0 || afterScheme[0] is '/' or '?' or '#')
            return "The URL has no host name.";
        return Uri.TryCreate(trimmed, UriKind.Absolute, out _)
            ? "The URL has characters that aren't allowed."
            : "The host name has characters that aren't allowed, or the port isn't a number.";
    }

    private static RequestFailure DescribeBodyFile(BodyFileException bodyFile)
    {
        var path = bodyFile.Path;
        var facts = bodyFile.InnerException switch
        {
            // A folder can't be opened as a file; on some systems that is reported as missing access.
            _ when Directory.Exists(path) => $"{path} is a folder, not a file.",
            FileNotFoundException or DirectoryNotFoundException => $"{path} doesn't exist.",
            UnauthorizedAccessException => $"You don't have permission to read {path}.",
            _ => $"{path} can't be opened. Another program may be using it.",
        };
        return new RequestFailure
        {
            Kind = RequestFailureKind.BodyFileUnreadable,
            Headline = "Body file can't be read",
            Facts = facts,
            NothingWasSent = true,
            OriginalMessage = ExceptionText.Flatten(bodyFile),
            Actions = [RequestFailureAction.ChooseFile],
            Subject = path,
        };
    }

    private static RequestFailure DescribeInvalidHeader(InvalidHeaderException header)
    {
        // The value of a credential header isn't repeated on screen or in a copied report.
        var value = SensitiveHeaders.IsSensitiveName(header.HeaderName) ? SensitiveHeaders.MaskText : ShowControlCharacters(header.HeaderValue);
        var (headline, facts) = header.Problem switch
        {
            InvalidHeaderProblem.Name => ("Header name isn't valid",
                $"The header name \"{header.HeaderName}\" can't be sent. Names can't have spaces, colons or other special characters."),
            InvalidHeaderProblem.NonAsciiValue => ("Header value isn't valid",
                $"Header {header.HeaderName} has characters outside ASCII, which a header can't carry: {value}"),
            _ => ("Header value isn't valid", $"Header {header.HeaderName} has a value that can't be sent: {value}"),
        };
        return new RequestFailure
        {
            Kind = RequestFailureKind.InvalidHeader,
            Headline = headline,
            Facts = facts,
            NothingWasSent = true,
            Details = [new FailureDetail("Header", header.HeaderName), new FailureDetail("Value", value)],
            Actions = [RequestFailureAction.ShowHeaders],
            Subject = header.HeaderName,
        };
    }

    /// <summary>Line breaks and other control characters written out, so the value stays on one line on screen.</summary>
    private static string ShowControlCharacters(string value) =>
        string.Concat(value.Select(character => character switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            _ when char.IsControl(character) && character != '\t' => $"\\u{(int)character:x4}",
            _ => character.ToString(),
        }));
}
