using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>UI-level services (dialogs, pickers, clipboard, browser) for view models.</summary>
public sealed class DialogService
{
    public Window? Owner { get; set; }

    private readonly Stack<Window> _openDialogs = new();

    private TopLevel Top => Owner ?? throw new InvalidOperationException("Main window not set.");

    /// <summary>
    /// Shows the dialog modal over the dialog opened most recently and still open, or over the main window when none
    /// is open, so a dialog opened from inside another one stacks on it.
    /// </summary>
    public async Task<T?> ShowDialogAsync<T>(Window dialog)
    {
        var owner = _openDialogs.Count > 0 ? _openDialogs.Peek() : Top as Window ?? throw new InvalidOperationException();
        _openDialogs.Push(dialog);
        try
        {
            return await dialog.ShowDialog<T?>(owner);
        }
        finally
        {
            _openDialogs.Pop();
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete", bool destructive = true)
    {
        var dialog = new MessageDialog(title, message, [(confirmText, "confirm", destructive ? "danger" : "accent"), ("Cancel", "cancel", "")]);
        return await ShowDialogAsync<string>(dialog) == "confirm";
    }

    /// <summary>Secrets are left out of exports unless the user says otherwise (closing the dialog leaves them out).</summary>
    public async Task<bool> AskIncludeSecretsAsync()
    {
        var dialog = new MessageDialog("Include secret values?",
            "This export contains secret variables or credentials. Left out, they are exported blank and the file is safe to share.",
            [("Include secrets", "include", "accent"), ("Leave out", "omit", "")]);
        return await ShowDialogAsync<string>(dialog) == "include";
    }

    /// <summary>Warns that collection and folder headers become request headers in a Postman export. False when cancelled.</summary>
    public async Task<bool> ConfirmExportWithInheritedHeadersAsync()
    {
        var dialog = new MessageDialog("Export headers to Postman?",
            "Postman has no folder headers, so collection and folder headers will be copied into each request and will not come back as folder headers when imported.",
            [("Export", "export", "accent"), ("Cancel", "cancel", "")]);
        return await ShowDialogAsync<string>(dialog) == "export";
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message, [("OK", "ok", "accent")]);
        await ShowDialogAsync<string>(dialog);
    }

    /// <summary>
    /// Shows storage errors in plain words with their next steps, Reveal file / Move aside where they apply, and the
    /// raw text under Details / Copy details. Use this for every storage error the user should see.
    /// </summary>
    public async Task ShowStorageErrorsAsync(string title, IReadOnlyList<StorageError> errors) =>
        await ShowDialogAsync<StorageDialogResult>(new StorageErrorDialog(title, errors, []));

    /// <summary>
    /// Tells the user an event handler failed. Never throws, so it is safe to call from a catch block of an
    /// <c>async void</c> handler, where an escaping exception would end the app.
    /// </summary>
    public async Task ReportUnexpectedErrorAsync(string action, Exception exception)
    {
        try
        {
            await ShowMessageAsync("Something went wrong", $"{action} failed: {exception.Message}");
        }
        catch (Exception)
        {
            // Nothing else can report it; losing the message is better than ending the app.
        }
    }

    public async Task<SaveChangesResult> AskSaveChangesAsync(string name)
    {
        var dialog = new MessageDialog("Unsaved changes",
            $"\"{name}\" has unsaved changes. Do you want to save them before closing?",
            [("Don't save", "discard", ""), ("Cancel", "cancel", ""), ("Save changes", "save", "accent")]);
        return await ShowDialogAsync<string>(dialog) switch
        {
            "save" => SaveChangesResult.Save,
            "discard" => SaveChangesResult.Discard,
            _ => SaveChangesResult.Cancel,
        };
    }

    public async Task<string?> PromptAsync(string title, string label, string initial = "", string okText = "Create")
    {
        var dialog = new PromptDialog(title, label, initial, okText);
        return await ShowDialogAsync<string>(dialog);
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FilePickerFileType[] types)
    {
        var files = await Top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = types.Length > 0 ? types : null,
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFileAsync(string title) => (await PickFilesAsync(title, false)).FirstOrDefault();

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await Top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string extension)
    {
        var file = await Top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task CopyTextAsync(string text)
    {
        if (Top.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>Opens the URL in the system browser. False when no browser could be launched.</summary>
    public async Task<bool> OpenUrlAsync(Uri uri) => await Top.Launcher.LaunchUriAsync(uri);

    public async Task RevealFolderAsync(string path) => await Top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));

    public static FilePickerFileType JsonFiles { get; } = new("JSON files") { Patterns = ["*.json"], MimeTypes = ["application/json"] };
}
