using CommunityToolkit.Mvvm.Input;

namespace Hermes.App.ViewModels;

/// <summary>Something with a name that the user can edit in place (see <c>InlineRenameBox</c>).</summary>
public interface IRenamable
{
    /// <summary>The current name, shown in the box when editing starts.</summary>
    string Name { get; }

    /// <summary>Whether the name is being edited right now.</summary>
    bool IsRenaming { get; set; }

    /// <summary>
    /// Ends the edit; the parameter is the entered text, or null when the edit was cancelled. An async command, as
    /// saving the new name can take a while.
    /// </summary>
    IAsyncRelayCommand<string?> CommitRenameCommand { get; }
}
