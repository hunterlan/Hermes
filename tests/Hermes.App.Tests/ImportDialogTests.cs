using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

public class ImportDialogTests
{
    /// <summary>Room that must stay between the footer buttons and the window's bottom edge, so rounding, DPI or fonts can't push them out.</summary>
    private const double MinimumSlackBelowButtons = 16;

    /// <summary>Height of the "Or paste raw text" label and its gap, which ImportPasteMinHeight includes but the box itself doesn't.</summary>
    /// <summary>Height the layout must have left over at its worst case (9 files, 5 errors).</summary>
    private const double MinimumSlackInBudget = 24;

    private const double PasteLabelHeight = 21;

    [AvaloniaFact]
    public async Task A_missing_file_is_reported_in_plain_words()
    {
        var vm = new ImportDialogViewModel(new DialogService());
        var missing = Path.Combine(Path.GetTempPath(), "hermes-missing-" + Guid.NewGuid() + ".json");
        vm.AddFiles([missing]);

        await vm.ImportCommand.ExecuteAsync(null);

        var error = Assert.Single(vm.Errors);
        Assert.Equal($"{Path.GetFileName(missing)} couldn't be loaded: it or its folder wasn't found.", error.Summary);
        Assert.DoesNotContain("Could not find", error.Message);
        Assert.True(vm.HasErrors);
        Assert.True(vm.HasDetails);
    }

    [AvaloniaFact]
    public async Task A_bug_shows_a_plain_sentence_and_keeps_the_raw_text_behind_a_details_button()
    {
        var vm = new ImportDialogViewModel(new DialogService())
        {
            // Repeated names make System.Text.Json throw ArgumentException when the object is read.
            Text = """{ "info": { "name": "a", "name": "b" }, "item": [] }""",
        };
        var dialog = new ImportDialog { DataContext = vm };
        dialog.Show();

        await vm.ImportCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("Hermes couldn't import Pasted text. This is a bug; please report it.", texts);
        Assert.DoesNotContain(texts, text => text?.Contains("ArgumentException") == true);
        var details = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Details");
        Assert.True(details.IsVisible);
        Assert.Empty(dialog.GetVisualDescendants().OfType<CollapsibleSection>());
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task An_error_without_raw_text_has_no_details()
    {
        var vm = new ImportDialogViewModel(new DialogService()) { Text = "curl -s" };

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Pasted text: The cURL command has no URL. Nothing was imported from it.", Assert.Single(vm.Errors).Summary);
        Assert.False(vm.HasDetails);
    }

    [AvaloniaFact]
    public void Many_files_and_errors_keep_the_buttons_in_the_window_and_off_the_paste_box()
    {
        var vm = new ImportDialogViewModel(new DialogService());
        vm.AddFiles(Enumerable.Range(1, 9).Select(index => $"/tmp/collection-{index}.json"));
        vm.ShowErrors(Enumerable.Range(1, 5).Select(index =>
            StorageError.Plain($"collection-{index}.json couldn't be loaded: it or its folder wasn't found.", details: "System.IO.FileNotFoundException: raw text")));
        var dialog = new ImportDialog { DataContext = vm };
        dialog.Show();
        UiTestHelpers.Pump();

        var clientBounds = new Rect(dialog.Bounds.Size);
        var import = BoundsInDialog(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Import"));
        var cancel = BoundsInDialog(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel"));
        var detailsButton = BoundsInDialog(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Details"));
        var pasteBox = BoundsInDialog(dialog, dialog.GetVisualDescendants().OfType<TextBox>().Single());
        var errorLines = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Text?.Contains("couldn't be loaded") == true)
            .Select(text => BoundsInDialog(dialog, text));

        Assert.True(clientBounds.Contains(import.TopLeft) && clientBounds.Contains(import.BottomRight), $"Import {import} outside {clientBounds}");
        Assert.True(clientBounds.Contains(cancel.TopLeft) && clientBounds.Contains(cancel.BottomRight), $"Cancel {cancel} outside {clientBounds}");
        Assert.True(clientBounds.Contains(detailsButton.TopLeft) && clientBounds.Contains(detailsButton.BottomRight), $"Details {detailsButton} outside {clientBounds}");
        Assert.True(clientBounds.Bottom - Math.Max(import.Bottom, cancel.Bottom) >= MinimumSlackBelowButtons,
            $"Buttons {import} / {cancel} leave less than {MinimumSlackBelowButtons}px below them in {clientBounds}");
        var minimumPasteHeight = (double)dialog.FindResource("ImportPasteMinHeight")!;
        // With everything at its cap the paste box must still be larger than its minimum by the slack, which is what keeps
        // the buttons in the window under rounding, DPI or font differences.
        Assert.True(pasteBox.Height >= minimumPasteHeight - PasteLabelHeight + MinimumSlackInBudget,
            $"Paste box {pasteBox} leaves less than {MinimumSlackInBudget}px of slack");
        Assert.All(errorLines, line => Assert.False(line.Intersects(pasteBox), $"Error {line} overlaps paste box {pasteBox}"));
        dialog.Close();
    }

    private static Rect BoundsInDialog(ImportDialog dialog, Visual visual) =>
        new(visual.TranslatePoint(new Point(0, 0), dialog) ?? default, visual.Bounds.Size);
}
