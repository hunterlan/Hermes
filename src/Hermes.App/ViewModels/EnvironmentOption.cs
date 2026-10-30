using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Entry in the environment selector; <see cref="Model"/> is null for "No environment".</summary>
public sealed record EnvironmentOption(string Name, EnvironmentModel? Model, bool IsActive = false)
{
    public bool IsNone => Model is null;
    /// <summary>"No environment" reads muted unless it is the active choice.</summary>
    public bool IsMuted => IsNone && !IsActive;
    public bool AsksFirst => Model?.ConfirmBeforeSend == true;
    public EnvironmentColor Color => Model?.Color ?? EnvironmentColor.None;

    public override string ToString() => Name;
}
