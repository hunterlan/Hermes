namespace Hermes.Core.Http;

/// <summary>The message of an exception and the exceptions it wraps, as one text.</summary>
public static class ExceptionText
{
    /// <summary>
    /// The messages along the chain, outermost first, leaving out a message that is already part of an earlier
    /// one: .NET often repeats the inner message in the outer one ("Connection refused (host:80) (Connection refused)").
    /// </summary>
    public static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (message.Length > 0 && !messages.Any(earlier => earlier.Contains(message, StringComparison.OrdinalIgnoreCase)))
                messages.Add(message);
        }
        return string.Join(" ", messages);
    }

    /// <summary>The message of the innermost exception, which is usually the one that says what actually failed.</summary>
    public static string Innermost(Exception exception)
    {
        var innermost = exception;
        while (innermost.InnerException is { } inner)
            innermost = inner;
        return innermost.Message;
    }
}
