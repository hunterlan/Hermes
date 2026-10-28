using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Hermes.Core.Formatting;

public static class XmlFormatter
{
    public static bool TryFormat(string text, out string formatted)
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
}
