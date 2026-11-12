namespace Hermes.App.Views.Dialogs;

/// <summary>An extra button of the storage error dialog, such as "Use default folder".</summary>
/// <param name="Label">Button text.</param>
/// <param name="Id">Returned in <see cref="StorageDialogResult.ChoiceId"/> when it's clicked.</param>
/// <param name="Class">Style class of the button (<c>accent</c>, <c>danger</c>), or empty.</param>
/// <param name="PicksFolder">When true, a folder picker opens first and the choice is made only if a folder is picked.</param>
public sealed record StorageDialogChoice(string Label, string Id, string Class = "", bool PicksFolder = false);
