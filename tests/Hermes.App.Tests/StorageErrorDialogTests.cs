using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

public class StorageErrorDialogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());

    private static Button ButtonNamed(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.IsVisible && Equals(button.Content, text));

    private static void Click(Button button)
    {
        // A click handler runs on the event; a bound command runs only through Button's own click logic.
        if (button.Command is { } command)
            command.Execute(button.CommandParameter);
        else
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UiTestHelpers.Pump();
    }

    [AvaloniaFact]
    public void The_dialog_shows_the_sentence_and_next_step_and_keeps_the_raw_text_for_details()
    {
        var error = StorageErrorDescriber.Describe(new IOException("No space left on device", unchecked((int)0x80070070)),
            Path.Combine(_dir, "a.json"), "Collection \"Pets\"", StorageOperation.Write);
        var dialog = new StorageErrorDialog("Storage problem", [error], []);
        dialog.Show();
        UiTestHelpers.Pump();

        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains(error.Summary, texts);
        Assert.Contains("Free up disk space, then try again.", texts);
        Assert.DoesNotContain(texts, text => text?.Contains("No space left") == true);
        Assert.Contains("No space left on device", ((StorageErrorDialogViewModel)dialog.DataContext!).DetailsText);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Details_are_a_collapsed_section()
    {
        var dialog = new StorageErrorDialog("Storage problem", [StorageError.Plain("Something happened.")], []);
        dialog.Show();
        UiTestHelpers.Pump();

        var section = dialog.GetVisualDescendants().OfType<Hermes.App.Controls.CollapsibleSection>().Single();
        Assert.Equal("Details", section.Header);
        Assert.False(section.IsExpanded);
        dialog.Close();
    }

    [Fact]
    public void The_default_folder_is_offered_only_when_another_folder_failed()
    {
        var other = StartupFolderChoices.For(Path.Combine(Path.GetTempPath(), "somewhere-else"));
        Assert.Equal([StartupFolderChoices.UseDefaultFolder, StartupFolderChoices.ChooseFolder], other.Select(choice => choice.Id));

        var sameFolder = StartupFolderChoices.For(AppPaths.DefaultDataDirectory + Path.DirectorySeparatorChar);
        Assert.Equal([StartupFolderChoices.ChooseFolder], sameFolder.Select(choice => choice.Id));
        Assert.True(sameFolder.Single().PicksFolder);
    }

    [AvaloniaFact]
    public void Move_aside_renames_the_corrupt_file_and_hides_the_button()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, "{ \"name\": \"Pets\", ");
        var error = StorageErrorDescriber.Describe(new System.Text.Json.JsonException(), file, "Collection \"Pets\"");
        var dialog = new StorageErrorDialog("Some files couldn't be loaded", [error], []);
        dialog.Show();
        UiTestHelpers.Pump();

        Click(ButtonNamed(dialog, "Move aside"));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(file + ".corrupt"));
        Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Button>(),
            button => button.IsVisible && Equals(button.Content, "Move aside"));
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_choice_closes_the_dialog_with_its_id()
    {
        var error = StorageError.Plain("Data folder can't be used.");
        var dialog = new StorageErrorDialog("Hermes can't start", [error],
            [new StorageDialogChoice("Use default folder", "default-folder")], closeText: "Quit");
        dialog.Show();
        UiTestHelpers.Pump();

        Click(ButtonNamed(dialog, "Use default folder"));

        Assert.Equal("default-folder", dialog.Result?.ChoiceId);
    }

    [AvaloniaFact]
    public void An_error_without_a_location_offers_no_file_actions()
    {
        var dialog = new StorageErrorDialog("Storage problem", [StorageError.Plain("Something happened.")], []);
        dialog.Show();
        UiTestHelpers.Pump();

        var visibleButtons = dialog.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsVisible).Select(button => button.Content as string).ToList();
        Assert.DoesNotContain("Reveal file", visibleButtons);
        Assert.DoesNotContain("Move aside", visibleButtons);
        Assert.Contains("Copy details", visibleButtons);
        dialog.Close();
    }

    [AvaloniaFact]
    [UnsupportedOSPlatform("windows")]
    public void An_unusable_data_subfolder_shows_the_startup_dialog_naming_it_with_the_three_choices()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Folder permissions are set with Unix modes here.");
        var collections = Path.Combine(_dir, "collections");
        Directory.CreateDirectory(collections);
        File.SetUnixFileMode(collections, UnixFileMode.None);
        try
        {
            var error = DataFolderCheck.FindUnusableSubfolder(_dir);
            Assert.SkipWhen(error is null, "Running with rights that ignore folder permissions (root).");

            var dialog = new StorageErrorDialog("Hermes can't start", [error!], StartupFolderChoices.For(_dir), closeText: "Quit");
            dialog.Show();
            UiTestHelpers.Pump();

            var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains(texts, text => text?.Contains($"Collections folder \"{collections}\"") == true);
            Assert.Contains(texts, text => text?.Contains("your collections stay in this one") == true);
            var visibleButtons = dialog.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsVisible).Select(button => button.Content as string).ToList();
            Assert.Contains("Use default folder", visibleButtons);
            Assert.Contains("Choose another folder", visibleButtons);
            Assert.Contains("Quit", visibleButtons);
            dialog.Close();
        }
        finally
        {
            File.SetUnixFileMode(collections, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }
}
