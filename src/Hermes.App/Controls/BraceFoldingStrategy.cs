using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace Hermes.App.Controls;

/// <summary>Folds multi-line { } and [ ] blocks, ignoring brackets inside strings.</summary>
public static class BraceFoldingStrategy
{
    public static void UpdateFoldings(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(CreateFoldings(document), -1);

    private static IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();
        var text = document.Text;
        var inString = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    stack.Push(i);
                    break;
                case '}' or ']' when stack.Count > 0:
                    var start = stack.Pop();
                    if (document.GetLineByOffset(start).LineNumber != document.GetLineByOffset(i).LineNumber)
                        foldings.Add(new NewFolding(start, i + 1));
                    break;
            }
        }
        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}
