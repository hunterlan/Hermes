using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public sealed partial class VariableRowViewModel(VariableListViewModel owner) : ObservableObject
{
    [ObservableProperty] public partial bool Enabled { get; set; } = true;
    [ObservableProperty] public partial string Key { get; set; } = "";
    [ObservableProperty] public partial string InitialValue { get; set; } = "";
    [ObservableProperty] public partial string CurrentValue { get; set; } = "";
    [ObservableProperty] public partial bool IsSecret { get; set; }
    [ObservableProperty] public partial bool IsRevealed { get; set; }
    [ObservableProperty] public partial bool IsPlaceholder { get; set; }

    /// <summary>Masks secret values unless revealed. Empty string = no masking.</summary>
    public char PasswordChar => IsSecret && !IsRevealed ? '•' : '\0';

    public bool CanRemove => !IsPlaceholder;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsSecret) or nameof(IsRevealed))
            OnPropertyChanged(nameof(PasswordChar));
        if (e.PropertyName == nameof(IsPlaceholder))
            OnPropertyChanged(nameof(CanRemove));
        if (e.PropertyName is nameof(Enabled) or nameof(Key) or nameof(InitialValue) or nameof(CurrentValue) or nameof(IsSecret))
            owner.OnRowChanged(this);
    }

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    public Variable ToModel() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = InitialValue,
        // Current value equal to the initial one isn't an override.
        CurrentValue = CurrentValue.Length == 0 || CurrentValue == InitialValue ? null : CurrentValue,
        Type = IsSecret ? VariableType.Secret : VariableType.Default,
    };
}

/// <summary>Editable variables table (environment, globals, collection variables).</summary>
public sealed partial class VariableListViewModel : ObservableObject
{
    private bool _loading;

    public VariableListViewModel() => AddPlaceholder();

    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; } = "";

    public event Action? Changed;

    public void Load(IEnumerable<Variable> variables)
    {
        _loading = true;
        Rows.Clear();
        foreach (var v in variables)
        {
            Rows.Add(new VariableRowViewModel(this)
            {
                Enabled = v.Enabled,
                Key = v.Key,
                InitialValue = v.Value,
                CurrentValue = v.CurrentValue ?? v.Value,
                IsSecret = v.Type == VariableType.Secret,
            });
        }
        AddPlaceholder();
        _loading = false;
    }

    public List<Variable> ToModels() => Rows.Where(r => !r.IsPlaceholder).Select(r => r.ToModel()).ToList();

    internal void OnRowChanged(VariableRowViewModel row)
    {
        if (_loading)
            return;
        if (row.IsPlaceholder && (row.Key.Length > 0 || row.InitialValue.Length > 0 || row.CurrentValue.Length > 0))
        {
            row.IsPlaceholder = false;
            AddPlaceholder();
        }
        Changed?.Invoke();
    }

    internal void Remove(VariableRowViewModel row)
    {
        if (row.IsPlaceholder)
            return;
        Rows.Remove(row);
        Changed?.Invoke();
    }

    private void AddPlaceholder()
    {
        var wasLoading = _loading;
        _loading = true;
        Rows.Add(new VariableRowViewModel(this) { IsPlaceholder = true });
        _loading = wasLoading;
    }

    /// <summary>Resets every current value to its initial value (Postman's "Reset all").</summary>
    [RelayCommand]
    private void ResetCurrentValues()
    {
        foreach (var row in Rows.Where(r => !r.IsPlaceholder))
            row.CurrentValue = row.InitialValue;
    }
}
