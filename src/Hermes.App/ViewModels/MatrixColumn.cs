using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>A column of the matrix: globals (the fallback for every environment) or one environment.</summary>
public sealed class MatrixColumn(EnvironmentModel model, bool isGlobals, bool isActive)
{
    public EnvironmentModel Model { get; } = model;
    public bool IsGlobals { get; } = isGlobals;
    public bool IsActive { get; } = isActive;
    public string Name => IsGlobals ? "Globals" : Model.Name;
    public string Caption => IsGlobals ? "fallback" : IsActive ? "active" : Model.ConfirmBeforeSend ? "asks before sending" : "";
    public bool ConfirmBeforeSend => Model.ConfirmBeforeSend;
}
