using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Storage;

namespace Hermes.App.Views.Dialogs;

/// <summary>
/// Shows storage errors as a plain sentence and next step each, with Reveal file / Move aside where they apply, the raw
/// text under Details and a Copy details button. Closes with the <see cref="StorageDialogResult"/> of the clicked
/// <see cref="StorageDialogChoice"/>, or null when it is just closed.
/// </summary>
public partial class StorageErrorDialog : Window
{
    /// <summary>The choice made, or null when the dialog was closed without one. Read it once the dialog has closed.</summary>
    public StorageDialogResult? Result { get; private set; }

    public StorageErrorDialog() : this("Storage problem", [], []) { }

    /// <param name="title">Dialog title and heading.</param>
    /// <param name="errors">The errors to list.</param>
    /// <param name="choices">Extra buttons that end the dialog with a result, or empty for a plain notice.</param>
    /// <param name="closeText">Text of the button that closes the dialog without a choice.</param>
    public StorageErrorDialog(string title, IReadOnlyList<StorageError> errors,
        IReadOnlyList<StorageDialogChoice> choices, string closeText = "Close")
    {
        InitializeComponent();
        Title = title;
        DataContext = new StorageErrorDialogViewModel(errors,
            path => FileRevealer.RevealAsync(this, path),
            async text =>
            {
                if (Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(text);
            });
        Header.Title = title;
        Footer.CloseButtonText = closeText;
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label };
            if (choice.Class.Length > 0)
                button.Classes.Add(choice.Class);
            button.Click += async (_, _) => await ChooseAsync(choice);
            ChoicePanel.Children.Add(button);
        }
    }

    private async Task ChooseAsync(StorageDialogChoice choice)
    {
        if (!choice.PicksFolder)
        {
            Result = new StorageDialogResult(choice.Id);
            Close(Result);
            return;
        }
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = choice.Label });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder)
            {
                Result = new StorageDialogResult(choice.Id, folder);
                Close(Result);
            }
        }
        catch (Exception exception)
        {
            await ReportFolderPickFailureAsync(exception);
        }
    }

    /// <summary>
    /// Tells the user the folder picker failed, in a dialog owned by this one so it works before any main window
    /// exists. Never throws: this runs inside an <c>async void</c> click handler.
    /// </summary>
    private async Task ReportFolderPickFailureAsync(Exception exception)
    {
        try
        {
            var notice = new MessageDialog("Folder picker failed",
                $"Hermes couldn't open the folder picker: {exception.Message}", [("OK", "ok", "accent")]);
            await notice.ShowDialog<string?>(this);
        }
        catch (Exception)
        {
            // Nothing else can report it; the dialog stays open so the user can pick another choice.
        }
    }
}
