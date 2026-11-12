namespace Hermes.App.Views.Dialogs;

/// <summary>What the user chose in the storage error dialog.</summary>
/// <param name="ChoiceId">The <see cref="StorageDialogChoice.Id"/> clicked.</param>
/// <param name="Folder">The folder picked, for a choice that <see cref="StorageDialogChoice.PicksFolder"/>.</param>
public sealed record StorageDialogResult(string ChoiceId, string? Folder = null);
