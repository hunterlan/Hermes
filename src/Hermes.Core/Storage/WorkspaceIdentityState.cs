namespace Hermes.Core.Storage;

/// <summary>A data folder's identity as found on opening it.</summary>
/// <param name="Id">The id to use from now on.</param>
/// <param name="IsNew">The folder had no id yet.</param>
/// <param name="CopiedFrom">The folder is a copy of another one that still exists; this is that folder's id.</param>
/// <param name="NeedsWrite">workspace.json has to be (re)written with <paramref name="Id"/> and the folder's path.</param>
public sealed record WorkspaceIdentityState(Guid Id, bool IsNew, Guid? CopiedFrom, bool NeedsWrite);
