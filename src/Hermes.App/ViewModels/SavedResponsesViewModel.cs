using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

/// <summary>The responses saved with a request, and which of them is on screen. The response pane shows the one picked.</summary>
public sealed partial class SavedResponsesViewModel : ObservableObject
{
    /// <summary>Raised when a saved response is to be displayed, with that response.</summary>
    public event EventHandler<SavedResponse>? ShowRequested;

    public ObservableCollection<SavedResponseItem> Items { get; } = [];

    /// <summary>Set while an earlier saved response (not the latest) is on screen.</summary>
    [ObservableProperty] public partial SavedResponseItem? ViewingEarlier { get; set; }

    public bool HasItems => Items.Count > 0;
    public string Label => Items.Count switch
    {
        0 => "",
        1 => "Saved",
        var n => $"Saved · {n - 1} earlier {(n == 2 ? "response" : "responses")}",
    };

    /// <summary>Replaces the saved list; with <paramref name="show"/> the newest one is displayed (e.g. when a tab opens).</summary>
    public void SetSaved(IReadOnlyList<SavedResponse> saved, bool show)
    {
        Items.Clear();
        for (var i = 0; i < saved.Count; i++)
            Items.Add(new SavedResponseItem(saved[i], i == 0));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Label));
        ViewingEarlier = null;
        if (show && saved.Count > 0)
            ShowRequested?.Invoke(this, saved[0]);
    }

    /// <summary>Puts a just-saved response in front of the list, keeping as many as the workspace stores.</summary>
    public void AddSaved(SavedResponse saved) =>
        SetSaved([saved, .. Items.Select(i => i.Saved).Take(WorkspaceRepository.MaxSavedResponses - 1)], show: false);

    [RelayCommand]
    private void ShowSaved(SavedResponseItem item)
    {
        ShowRequested?.Invoke(this, item.Saved);
        ViewingEarlier = item.IsLatest ? null : item;
    }
}
