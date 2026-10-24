using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Hermes.App.Views.Dialogs;

namespace Hermes.App.Services;

/// <summary>UI-level services (dialogs, pickers, clipboard, browser) for view models.</summary>
public sealed class DialogService
{
    public Window? Owner { get; set; }

    private TopLevel Top => Owner ?? throw new InvalidOperationException("Main window not set.");

    public Task<T?> ShowDialogAsync<T>(Window dialog) => dialog.ShowDialog<T?>(Top as Window ?? throw new InvalidOperationException());

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

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message, [("OK", "ok", "accent")]);
        await ShowDialogAsync<string>(dialog);
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
