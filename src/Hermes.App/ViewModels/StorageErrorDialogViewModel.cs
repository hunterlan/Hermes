using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

/// <summary>The errors shown by the storage error dialog, and its Details / Copy details.</summary>
public sealed partial class StorageErrorDialogViewModel(
    IReadOnlyList<StorageError> errors,
    Func<string, Task> reveal,
    Func<string, Task> copy) : ObservableObject
{
    public IReadOnlyList<StorageErrorItemViewModel> Items { get; } =
        errors.Select(error => new StorageErrorItemViewModel(error, reveal)).ToList();

    /// <summary>The raw text of every error, separated by blank lines.</summary>
    public string DetailsText { get; } = string.Join("\n\n", errors.Select(error => error.Details));

    [ObservableProperty]
    public partial string CopyDetailsText { get; private set; } = "Copy details";

    [RelayCommand]
    private async Task CopyDetails()
    {
        await copy(DetailsText);
        CopyDetailsText = "Copied!";
    }
}
