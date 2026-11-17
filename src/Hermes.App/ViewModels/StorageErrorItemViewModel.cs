using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

/// <summary>One storage error in the error dialog: its sentence, next step and the Reveal / Move aside actions.</summary>
public sealed partial class StorageErrorItemViewModel(StorageError error, Func<string, Task> reveal) : ObservableObject
{
    public StorageError Error { get; } = error;

    public string Summary => Error.Summary;
    public bool CanReveal => Error.Location is not null;

    /// <summary>What to do about it. Cleared once the file is moved aside, when there is nothing left to fix.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextStep))]
    public partial string? NextStep { get; private set; } = error.NextStep;

    /// <summary>"Reveal file" when there is a file to select; otherwise the file manager opens its folder.</summary>
    [ObservableProperty]
    public partial string RevealText { get; private set; } = RevealTextFor(error.Location);

    public bool HasNextStep => NextStep is not null;

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
            NextStep = null;
            RevealText = RevealTextFor(location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Outcome = StorageErrorDescriber.Describe(ex, location, "The file", StorageOperation.Write).Message;
        }
    }

    private static string RevealTextFor(string? location) =>
        location is not null && File.Exists(location) ? "Reveal file" : "Reveal folder";
}
