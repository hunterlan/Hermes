using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Entry in the environment selector; <see cref="Model"/> is null for "No Environment".</summary>
public sealed record EnvironmentOption(string Name, EnvironmentModel? Model)
{
    public override string ToString() => Name;
}
