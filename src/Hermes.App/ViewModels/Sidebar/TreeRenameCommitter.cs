namespace Hermes.App.ViewModels.Sidebar;

/// <summary>Ends an inline rename in the collections tree; completes once the new name is saved or put back.</summary>
public delegate Task TreeRenameCommitter(TreeNodeViewModel node, string? newName);
