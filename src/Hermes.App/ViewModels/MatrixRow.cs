using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public sealed partial class MatrixRow : ObservableObject
{
    public MatrixRow(string name, int usedBy)
    {
        Name = name;
        SavedName = name.Length > 0 ? name : null;
        UsedBy = usedBy;
    }

    [ObservableProperty] public partial string Name { get; set; }

    /// <summary>The variable's name in the environments, so renaming the row renames it; null for a new row.</summary>
    public string? SavedName { get; }

    /// <summary>Secret if any environment defines the variable as Secret, so a value added elsewhere is kept secret too.</summary>
    public VariableType Type => Cells.Any(c => c.IsSecret) ? VariableType.Secret : VariableType.Default;

    public int UsedBy { get; }
    public string UsedByText => UsedBy switch { 0 => "unused", 1 => "1 request", var n => $"{n} requests" };
    public ObservableCollection<MatrixCell> Cells { get; } = [];
}
