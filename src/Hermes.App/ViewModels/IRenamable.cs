namespace Hermes.App.ViewModels;

/// <summary>Something with a name that the user can edit in place (see <c>InlineRenameBox</c>).</summary>
public interface IRenamable
{
    /// <summary>The current name, shown in the box when editing starts.</summary>
    string Name { get; }

    /// <summary>Whether the name is being edited right now.</summary>
    bool IsRenaming { get; set; }

    /// <summary>Ends the edit. <paramref name="newName"/> is the entered text, or null when the edit was cancelled.</summary>
    void CommitRename(string? newName);
}
