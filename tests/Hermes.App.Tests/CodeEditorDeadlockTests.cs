using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaEdit;
using Hermes.App.Controls;

namespace Hermes.App.Tests;

/// <summary>
/// Regression tests for issue #29: TextMateSharp's tokenizer thread and the UI thread could take two locks in opposite
/// order when a <see cref="CodeEditor"/> changed its language while the tokenizer was publishing results.
/// </summary>
public class CodeEditorDeadlockTests
{
    private const int Iterations = 300;
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromSeconds(30);

    [AvaloniaFact]
    public void Switching_language_and_text_while_the_tokenizer_is_busy_does_not_deadlock()
    {
        var largeJson = BuildPhotosJson(5_000);
        var smallJson = "{\"a\": 1}";
        var editor = new CodeEditor { Language = "json" };
        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        var inner = Assert.IsType<TextEditor>(editor.Content);

        var progress = 0;
        var finished = false;
        // A stuck UI thread cannot fail the test by itself, so a watchdog ends the run instead of hanging it forever.
        var watchdog = new Thread(() =>
        {
            var lastSeen = -1;
            var lastChange = DateTime.UtcNow;
            while (!Volatile.Read(ref finished))
            {
                Thread.Sleep(250);
                var current = Volatile.Read(ref progress);
                if (current != lastSeen)
                {
                    lastSeen = current;
                    lastChange = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastChange > StuckThreshold)
                {
                    Environment.FailFast($"UI thread deadlocked in CodeEditor at iteration {current} (issue #29).");
                }
            }
        })
        { IsBackground = true };
        watchdog.Start();

        try
        {
            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                editor.Language = "json";
                editor.Text = largeJson;
                // Every Text set is an undo step that keeps the replaced text; 600 of them held about 1.4 GB.
                inner.Document.UndoStack.ClearAll();
                // Let the tokenizer thread start publishing results for the large document.
                Thread.Sleep(iteration % 5);
                editor.Language = iteration % 2 == 0 ? "text" : "xml";
                editor.Text = smallJson;
                inner.Document.UndoStack.ClearAll();
                editor.Language = "json";
                UiTestHelpers.Pump();
                Volatile.Write(ref progress, iteration + 1);
            }
        }
        finally
        {
            Volatile.Write(ref finished, true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Highlighting_is_off_for_plain_text_and_returns_after_switching_back_to_json()
    {
        const string json = "{\n  \"name\": \"hermes\",\n  \"count\": 42\n}";
        var editor = new CodeEditor { Language = "text", Text = json };
        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        var inner = Assert.IsType<TextEditor>(editor.Content);

        UiTestHelpers.Pump();
        Assert.Equal(1, DistinctForegroundCount(inner, 2));

        editor.Language = "json";
        Assert.True(WaitUntil(() => DistinctForegroundCount(inner, 2) > 1), "JSON was not highlighted.");

        editor.Language = "text";
        Assert.True(WaitUntil(() => DistinctForegroundCount(inner, 2) == 1), "Plain text is still highlighted.");

        editor.Language = "json";
        Assert.True(WaitUntil(() => DistinctForegroundCount(inner, 2) > 1), "JSON was not highlighted again.");
        window.Close();
    }

    // The tokenizer runs on its own thread, so highlighting shows up a little after the grammar is set.
    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            UiTestHelpers.Pump();
            if (condition())
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    private static int DistinctForegroundCount(TextEditor inner, int lineNumber)
    {
        var textView = inner.TextArea.TextView;
        textView.EnsureVisualLines();
        var visualLine = textView.GetOrConstructVisualLine(inner.Document.GetLineByNumber(lineNumber));
        return visualLine.Elements
            .Select(element => (element.TextRunProperties?.ForegroundBrush as ISolidColorBrush)?.Color)
            .Distinct()
            .Count();
    }

    private static string BuildPhotosJson(int count)
    {
        var builder = new StringBuilder("[\n");
        for (var index = 1; index <= count; index++)
        {
            builder.Append("  {\n")
                .Append("    \"albumId\": ").Append(index / 50 + 1).Append(",\n")
                .Append("    \"id\": ").Append(index).Append(",\n")
                .Append("    \"title\": \"accusamus beatae ad facilis cum similique qui sunt\",\n")
                .Append("    \"url\": \"https://via.placeholder.com/600/").Append(index.ToString("x6")).Append("\",\n")
                .Append("    \"thumbnailUrl\": \"https://via.placeholder.com/150/").Append(index.ToString("x6")).Append("\"\n")
                .Append(index == count ? "  }\n" : "  },\n");
        }
        return builder.Append(']').ToString();
    }
}
