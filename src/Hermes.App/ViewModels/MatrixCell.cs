using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>One variable in one environment.</summary>
public sealed partial class MatrixCell : ObservableObject
{
    private readonly Action _changed;

    public MatrixCell(MatrixColumn column, Variable? variable, Action changed)
    {
        Column = column;
        _changed = changed;
        IsSecret = variable?.Type == VariableType.Secret;
        Value = variable?.EffectiveValue ?? "";
        IsSet = variable is not null;
    }

    public MatrixColumn Column { get; }
    public bool IsSecret { get; }
    public bool IsActiveColumn => Column.IsActive;

    [ObservableProperty] public partial string Value { get; set; }

    /// <summary>The environment defines the variable (possibly empty); otherwise it falls back to globals.</summary>
    [ObservableProperty] public partial bool IsSet { get; set; }

    public string Placeholder => IsSet ? "Empty" : Column.IsGlobals ? "not set" : "inherits Globals";

    partial void OnIsSetChanged(bool value) => OnPropertyChanged(nameof(Placeholder));

    partial void OnValueChanged(string value)
    {
        IsSet = true;
        _changed();
    }
}
