using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

public sealed partial class EnvironmentItemViewModel(
    EnvironmentModel model,
    bool isGlobals,
    bool isActive,
    EnvironmentRenameCommitter renameCommitter) : ObservableObject, IRenamable
{
    public EnvironmentModel Model { get; } = model;
    public string Name => Model.Name;
    public bool IsGlobals { get; } = isGlobals;
    public bool IsActive { get; } = isActive;
    public bool IsRegular => !IsGlobals;
    public bool ConfirmBeforeSend => Model.ConfirmBeforeSend;
    public EnvironmentColor Color => Model.Color;

    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    /// <summary>Hands the edit to the pane that owns this item, which renames the model and saves.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CommitRename(string? newName) => renameCommitter(this, newName);
}
