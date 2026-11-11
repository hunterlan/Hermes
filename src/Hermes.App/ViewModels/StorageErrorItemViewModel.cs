using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

/// <summary>One storage error in the error dialog: its sentence, next step and the Reveal / Move aside actions.</summary>
public sealed partial class StorageErrorItemViewModel(StorageError error, Func<string, Task> reveal) : ObservableObject
{
    public StorageError Error { get; } = error;

    public string Summary => Error.Summary;
    public string? NextStep => Error.NextStep;
    public bool HasNextStep => Error.NextStep is not null;
    public bool CanReveal => Error.Location is not null;

    /// <summary>"Reveal file" when there is a file to select; otherwise the file manager opens its folder.</summary>
    public string RevealText => Error.Location is { } location && File.Exists(location) ? "Reveal file" : "Reveal folder";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutcome))]
    public partial string? Outcome { get; private set; }

    [ObservableProperty]
    public partial bool CanMoveAside { get; private set; } = error.CanMoveAside;

    public bool HasOutcome => Outcome is not null;

    [RelayCommand]
    private async Task Reveal()
    {
        if (Error.Location is { } location)
            await reveal(location);
    }

    [RelayCommand]
    private void MoveAside()
    {
        if (Error.Location is not { } location)
            return;
        try
        {
            var moved = CorruptFile.MoveAside(location);
            Outcome = $"Moved aside to {Path.GetFileName(moved)}. Hermes starts without it next time.";
            CanMoveAside = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Outcome = StorageErrorDescriber.Describe(ex, location, "The file", StorageOperation.Write).Message;
        }
    }
}
