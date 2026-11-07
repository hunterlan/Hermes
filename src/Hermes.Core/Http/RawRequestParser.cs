namespace Hermes.Core.Http;

/// <summary>
/// Splits the text made by <see cref="RawRequestFormatter"/> back into its request line, headers and body. Saved
/// responses keep only that text, so this is how their Sent request gets header rows.
/// </summary>
public static class RawRequestParser
{
    public static ParsedRawRequest Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var requestLine = lines[0];

        // Headers run from the second line to the first empty one; the body is what follows.
        var headers = new List<RawRequestHeader>();
        var index = 1;
        for (; index < lines.Length && lines[index].Length > 0; index++)
        {
            var colon = lines[index].IndexOf(':');
            if (colon > 0)
                headers.Add(new RawRequestHeader(lines[index][..colon], lines[index][(colon + 1)..].Trim()));
        }
        var body = index + 1 < lines.Length ? string.Join('\n', lines.Skip(index + 1)) : "";
        return new ParsedRawRequest(requestLine, headers, body);
    }
}
