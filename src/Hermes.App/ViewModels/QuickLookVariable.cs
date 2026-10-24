namespace Hermes.App.ViewModels;

/// <summary>Row in the environment quick-look popup.</summary>
public sealed record QuickLookVariable(string Key, string InitialValue, string CurrentValue);
