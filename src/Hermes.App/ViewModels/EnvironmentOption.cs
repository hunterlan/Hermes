using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Entry in the environment selector; <see cref="Model"/> is null for "No environment".</summary>
public sealed record EnvironmentOption(string Name, EnvironmentModel? Model, bool IsActive = false)
{
    public bool IsNone => Model is null;
    public bool AsksFirst => Model?.ConfirmBeforeSend == true;

    public override string ToString() => Name;
}
