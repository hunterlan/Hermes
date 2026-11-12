namespace Hermes.Core.Storage;

/// <summary>
/// A storage failure in words the user can act on: what happened (<see cref="Summary"/>), what to do about it
/// (<see cref="NextStep"/>) and the raw technical text (<see cref="Details"/>), which is shown only on request.
/// Build one from an exception with <see cref="StorageErrorDescriber"/>, or with <see cref="Plain"/> when no
/// exception is involved.
/// </summary>
/// <param name="Kind">What kind of failure this is.</param>
/// <param name="Summary">One plain sentence, with no exception type names or positions in bytes.</param>
/// <param name="NextStep">One plain sentence telling what to do, or null when there is nothing to suggest.</param>
/// <param name="Details">The raw message, exception type and location, for Details and Copy details.</param>
/// <param name="Location">The file or folder concerned; the Reveal action works on it. Null when there isn't one.</param>
/// <param name="CanMoveAside">True when <see cref="Location"/> is a corrupt file that can be renamed to <c>.corrupt</c>.</param>
public sealed record StorageError(
    StorageErrorKind Kind,
    string Summary,
    string? NextStep,
    string Details,
    string? Location = null,
    bool CanMoveAside = false)
{
    /// <summary>The summary followed by the next step, as one text.</summary>
    public string Message => NextStep is null ? Summary : $"{Summary} {NextStep}";

    /// <summary>An error that isn't about one exception, such as a refusal Hermes detected itself.</summary>
    public static StorageError Plain(string summary, string? nextStep = null, string? details = null, string? location = null) =>
        new(StorageErrorKind.Other, summary, nextStep, details ?? summary, location);
}
