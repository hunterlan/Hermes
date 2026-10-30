using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

/// <summary>Every variable across globals and all environments, edited side by side.</summary>
public sealed partial class EnvironmentMatrixTabViewModel : TabViewModelBase
{
    /// <summary>The matrix is a singleton tab.</summary>
    public static readonly Guid MatrixId = new("6d0b5f0e-1c55-4a8e-9b1f-3c2f1d7a9e01");

    private const double NameColumnWidth = 190;
    private const double UsedByColumnWidth = 110;
    private const double EnvironmentColumnMinWidth = 230;

    private string _snapshot = "";
    private bool _loading;

    public EnvironmentMatrixTabViewModel(TabServices services) : base(services)
    {
        Title = "Environments";
        Columns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TableMinWidth));
        Load();
    }

    public override SessionTabKind Kind => SessionTabKind.EnvironmentMatrix;
    public override Guid EntityId => MatrixId;
    public override string KindLabel => "ENV";

    public ObservableCollection<MatrixColumn> Columns { get; } = [];
    public ObservableCollection<MatrixRow> Rows { get; } = [];

    /// <summary>Narrowest the table can get before it scrolls horizontally: fixed columns plus each environment's minimum.</summary>
    public double TableMinWidth => NameColumnWidth + UsedByColumnWidth + Columns.Count * EnvironmentColumnMinWidth;

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
        RemoveDeletedColumns();
        foreach (var column in Columns)
        {
            var variables = column.Model.Variables;
            var rows = Rows.Where(r => r.Name.Trim().Length > 0).ToList();
            // Each row's variable is found before any is renamed, so swapped names or a new row taking a renamed
            // row's old name can't land on the wrong variable.
            var existing = rows.Where(r => r.SavedName is not null)
                .ToDictionary(r => r, r => variables.FirstOrDefault(v => v.Key == r.SavedName));
            foreach (var row in rows.Where(r => r.SavedName is null))
                existing[row] = variables.FirstOrDefault(v => v.Key == row.Name.Trim() && !existing.ContainsValue(v));

            foreach (var row in rows)
            {
                var cell = row.Cells.First(c => c.Column == column);
                if (!cell.IsSet)
                    continue;
                var name = row.Name.Trim();
                var variable = existing[row];
                if (variable is null)
                {
                    variables.Add(new Variable(name, cell.Value) { Type = row.Type });
                    continue;
                }
                variable.Key = name;
                if (variable.EffectiveValue != cell.Value)
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
        // Pick up environments added, renamed or edited elsewhere, unless the user has unsaved edits here. A deleted
        // environment goes either way: saving its column would write it back.
        if (!IsDirty)
            Load();
        else
            RemoveDeletedColumns();
    }

    private void RemoveDeletedColumns()
    {
        var workspace = Services.Workspace;
        foreach (var column in Columns.Where(c => !c.IsGlobals && !workspace.Environments.Contains(c.Model)).ToList())
        {
            Columns.Remove(column);
            foreach (var row in Rows)
                row.Cells.Remove(row.Cells.First(c => c.Column == column));
        }
        UpdateState();
    }
}
