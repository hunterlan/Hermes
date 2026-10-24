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
