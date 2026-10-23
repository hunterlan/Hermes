using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;

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

public sealed partial class MatrixRow : ObservableObject
{
    public MatrixRow(string name, int usedBy)
    {
        Name = name;
        UsedBy = usedBy;
    }

    [ObservableProperty] public partial string Name { get; set; }
    public int UsedBy { get; }
    public string UsedByText => UsedBy switch { 0 => "unused", 1 => "1 request", var n => $"{n} requests" };
    public ObservableCollection<MatrixCell> Cells { get; } = [];
}

/// <summary>Every variable across globals and all environments, edited side by side.</summary>
public sealed partial class EnvironmentMatrixTabViewModel : TabViewModelBase
{
    /// <summary>The matrix is a singleton tab.</summary>
    public static readonly Guid MatrixId = new("6d0b5f0e-1c55-4a8e-9b1f-3c2f1d7a9e01");

    private string _snapshot = "";
    private bool _loading;

    public EnvironmentMatrixTabViewModel(TabServices services) : base(services)
    {
        Title = "Environments";
        Load();
    }

    public override SessionTabKind Kind => SessionTabKind.EnvironmentMatrix;
    public override Guid EntityId => MatrixId;
    public override string KindLabel => "ENV";

    public ObservableCollection<MatrixColumn> Columns { get; } = [];
    public ObservableCollection<MatrixRow> Rows { get; } = [];

    /// <summary>Variables used by requests that would be sent empty or literally in the active environment.</summary>
    [ObservableProperty] public partial string? MissingWarning { get; set; }

    private void Load()
    {
        _loading = true;
        var workspace = Services.Workspace;
        Columns.Clear();
        Columns.Add(new MatrixColumn(workspace.Globals, isGlobals: true, isActive: false));
        foreach (var env in workspace.Environments)
            Columns.Add(new MatrixColumn(env, isGlobals: false, isActive: env == workspace.ActiveEnvironment));

        var usage = CountUsage();
        var names = Columns.SelectMany(c => c.Model.Variables).Select(v => v.Key).Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        Rows.Clear();
        foreach (var name in names)
            Rows.Add(CreateRow(name, usage.GetValueOrDefault(name)));
        _loading = false;
        _snapshot = Snapshot();
        UpdateState();
    }

    private MatrixRow CreateRow(string name, int usedBy)
    {
        var row = new MatrixRow(name, usedBy);
        foreach (var column in Columns)
            row.Cells.Add(new MatrixCell(column, column.Model.Variables.FirstOrDefault(v => v.Key == name), UpdateState));
        row.PropertyChanged += (_, _) => UpdateState();
        return row;
    }

    /// <summary>How many saved requests mention each variable.</summary>
    private Dictionary<string, int> CountUsage()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var request in Services.Workspace.Collections.SelectMany(c => Requests(c.Items)))
        {
            var json = ModelCloner.Serialize(request);
            foreach (var name in VariableResolver.FindVariables(json).Select(m => m.Name).Distinct())
                counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        return counts;

        static IEnumerable<HttpRequestModel> Requests(IEnumerable<CollectionNode> nodes) =>
            nodes.SelectMany(n => n switch
            {
                HttpRequestModel r => [r],
                FolderModel f => Requests(f.Items),
                _ => [],
            });
    }

    private void UpdateState()
    {
        if (_loading)
            return;
        IsDirty = Snapshot() != _snapshot;

        var active = Columns.FirstOrDefault(c => c.IsActive);
        var missing = active is null
            ? []
            : Rows.Where(r => r.UsedBy > 0 && r.Name.Length > 0 && !HasValue(r, active) && !HasValue(r, Columns[0])).Select(r => r.Name).ToList();
        MissingWarning = missing.Count == 0
            ? null
            : $"{string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} empty in {active!.Name}. Requests using {(missing.Count == 1 ? "it" : "them")} would send literal text; Hermes asks before sending.";

        static bool HasValue(MatrixRow row, MatrixColumn column) =>
            row.Cells.First(c => c.Column == column) is { IsSet: true, Value.Length: > 0 };
    }

    private string Snapshot() =>
        string.Join("\n", Rows.Select(r => r.Name + "=" + string.Join("|", r.Cells.Select(c => c.IsSet ? c.Value : "\0"))));

    [RelayCommand]
    private void AddVariable()
    {
        var row = CreateRow("", 0);
        Rows.Add(row);
        UpdateState();
    }

    public override Task<bool> SaveAsync()
    {
        foreach (var column in Columns)
        {
            var variables = column.Model.Variables;
            foreach (var row in Rows.Where(r => r.Name.Trim().Length > 0))
            {
                var cell = row.Cells.First(c => c.Column == column);
                if (!cell.IsSet)
                    continue;
                var name = row.Name.Trim();
                var variable = variables.FirstOrDefault(v => v.Key == name);
                if (variable is null)
                {
                    variables.Add(new Variable(name, cell.Value));
                }
                else if (variable.EffectiveValue != cell.Value)
                {
                    // Edit the value the user sees: the local current value if one is set, else the shared one.
                    if (variable.CurrentValue is not null)
                        variable.CurrentValue = cell.Value;
                    else
                        variable.Value = cell.Value;
                }
            }
            Services.Workspace.SaveEnvironment(column.Model);
        }
        Load();
        return Task.FromResult(true);
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    public override void OnVariablesChanged()
    {
        // Pick up environments added, renamed or edited elsewhere, unless the user has unsaved edits here.
        if (!IsDirty)
            Load();
    }
}
