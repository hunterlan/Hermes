namespace Hermes.App.ViewModels;

/// <summary>
/// Row in the environment quick-look popup: the value that is sent (secrets masked). <see cref="IsOverridden"/> marks
/// a global that the active environment replaces.
/// </summary>
public sealed record QuickLookVariable(string Key, string Value, bool IsOverridden = false)
{
    public bool IsEmpty => Value.Length == 0;
    public bool IsPlain => !IsEmpty && !IsOverridden;
}
