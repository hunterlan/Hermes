namespace Hermes.App.ViewModels.Sidebar;

/// <summary>Ends an inline rename in the environments list; completes once the new name is saved or put back.</summary>
public delegate Task EnvironmentRenameCommitter(EnvironmentItemViewModel item, string? newName);
