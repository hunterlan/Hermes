namespace Hermes.App.Services;

/// <summary>How to word a failed save of a collection when the save is part of something else, such as a rename.</summary>
/// <param name="Subject">What failed, as the start of a sentence: <c>Collection "Beta"</c>.</param>
/// <param name="FailedAction">What it couldn't do, as the middle of a sentence: <c>couldn't be renamed to "Gamma"</c>.</param>
public sealed record SaveFailureWording(string Subject, string FailedAction);
