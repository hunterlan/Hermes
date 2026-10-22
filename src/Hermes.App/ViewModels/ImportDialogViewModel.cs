using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Import;

namespace Hermes.App.ViewModels;

public sealed partial class ImportDialogViewModel(DialogService dialogs) : ObservableObject
{
    public ObservableCollection<string> Files { get; } = [];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ImportCommand))] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string? Error { get; set; }

    public bool HasFiles => Files.Count > 0;

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
            catch (IOException ex)
            {
                result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (Text.Trim().Length > 0)
            ImportService.ImportText(Text, result);

        if (result.IsEmpty)
        {
            Error = result.Errors.Count > 0 ? string.Join("\n", result.Errors) : "Nothing to import.";
            return;
        }
        CloseRequested?.Invoke(result);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
