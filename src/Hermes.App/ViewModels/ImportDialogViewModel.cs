using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Import;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

public sealed partial class ImportDialogViewModel(DialogService dialogs) : ObservableObject
{
    public ObservableCollection<string> Files { get; } = [];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ImportCommand))] public partial string Text { get; set; } = "";

    /// <summary>Why the last import attempt brought nothing in; empty until then.</summary>
    public ObservableCollection<StorageError> Errors { get; } = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasErrors => Errors.Count > 0;

    /// <summary>True when some error has raw text beyond its summary, so the Details button is worth showing.</summary>
    public bool HasDetails => Errors.Any(error => error.Details != error.Summary);

    public event Action<ImportResult?>? CloseRequested;

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!Files.Contains(path))
                Files.Add(path);
        OnPropertyChanged(nameof(HasFiles));
        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ChooseFiles()
    {
        AddFiles(await dialogs.PickFilesAsync("Import Postman collections or environments", allowMultiple: true, DialogService.JsonFiles));
    }

    [RelayCommand]
    private void RemoveFile(string path)
    {
        Files.Remove(path);
        OnPropertyChanged(nameof(HasFiles));
        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanImport() => Files.Count > 0 || Text.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        var result = new ImportResult();
        foreach (var file in Files)
        {
            try
            {
                ImportService.ImportText(await File.ReadAllTextAsync(file), result, Path.GetFileName(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add(StorageErrorDescriber.Describe(ex, file, Path.GetFileName(file), StorageOperation.Read));
            }
        }
        if (Text.Trim().Length > 0)
            ImportService.ImportText(Text, result);

        if (result.IsEmpty)
        {
            ShowErrors(result.Errors.Count > 0 ? result.Errors : [StorageError.Plain("Nothing to import.")]);
            return;
        }
        CloseRequested?.Invoke(result);
    }

    [RelayCommand]
    private async Task ShowDetails() =>
        await dialogs.ShowStorageErrorsAsync("Nothing was imported", Errors.ToList());

    /// <summary>Replaces the errors shown under the paste box.</summary>
    internal void ShowErrors(IEnumerable<StorageError> errors)
    {
        Errors.Clear();
        foreach (var error in errors)
            Errors.Add(error);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasDetails));
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
