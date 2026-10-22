using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Hermes.Core.Formatting;

public static class BodyFormatter
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
    public static string FormatJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch (JsonException)
        {
            return LenientJsonFormat(text);
        }
    }

    public static bool TryFormatXml(string text, out string formatted)
    {
        try
        {
            var doc = XDocument.Parse(text, LoadOptions.None);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = doc.Declaration is null,
            };
            using (var writer = XmlWriter.Create(sb, settings))
                doc.Save(writer);
            formatted = sb.ToString();
            return true;
        }
        catch (XmlException)
        {
            formatted = text;
            return false;
        }
    }

    public static string LenientJsonFormat(string text)
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
