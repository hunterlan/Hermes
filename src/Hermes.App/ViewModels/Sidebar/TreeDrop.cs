namespace Hermes.App.ViewModels.Sidebar;

/// <summary>A node dropped in the collections tree.</summary>
/// <param name="Dragged">The node being moved.</param>
/// <param name="Target">The node it was dropped on.</param>
/// <param name="Position">Where in the target it landed.</param>
public sealed record TreeDrop(TreeNodeViewModel Dragged, TreeNodeViewModel Target, DropPosition Position);
