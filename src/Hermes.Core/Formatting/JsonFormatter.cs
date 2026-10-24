using System.Text;
using System.Text.Json;

namespace Hermes.Core.Formatting;

public static class JsonFormatter
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Pretty-prints JSON. Falls back to a lenient, token-based formatter for text that isn't strictly valid
    /// (e.g. contains unquoted <c>{{variables}}</c>), so "Beautify" still works on request bodies.
    /// </summary>
    public static string Format(string text) =>
        TryFormat(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, out var formatted)
            ? formatted
            : FormatLenient(text);

    /// <summary>Pretty-prints <paramref name="text"/> if it is valid JSON; otherwise leaves it as it is.</summary>
    public static bool TryFormat(string text, out string formatted) => TryFormat(text, default, out formatted);

    private static bool TryFormat(string text, JsonDocumentOptions options, out string formatted)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, options);
            formatted = JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
            return true;
        }
        catch (JsonException)
        {
            formatted = text;
            return false;
        }
    }

    private static string FormatLenient(string text)
    {
        const string indentUnit = "  ";
        var sb = new StringBuilder(text.Length * 2);
        var indent = 0;
        var inString = false;

        void NewLine()
        {
            sb.Append('\n');
            for (var k = 0; k < indent; k++)
                sb.Append(indentUnit);
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                    sb.Append(text[++i]);
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;
                case '{' or '[':
                    // Keep {{variables}} intact.
                    if (c == '{' && i + 1 < text.Length && text[i + 1] == '{')
                    {
                        var end = text.IndexOf("}}", i, StringComparison.Ordinal);
                        if (end > 0)
                        {
                            sb.Append(text, i, end + 2 - i);
                            i = end + 1;
                            break;
                        }
                    }
                    sb.Append(c);
                    var close = c == '{' ? '}' : ']';
                    var j = i + 1;
                    while (j < text.Length && char.IsWhiteSpace(text[j]))
                        j++;
                    if (j < text.Length && text[j] == close)
                    {
                        sb.Append(close);
                        i = j;
                        break;
                    }
                    indent++;
                    NewLine();
                    break;
                case '}' or ']':
                    indent = Math.Max(0, indent - 1);
                    NewLine();
                    sb.Append(c);
                    break;
                case ',':
                    sb.Append(c);
                    NewLine();
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
