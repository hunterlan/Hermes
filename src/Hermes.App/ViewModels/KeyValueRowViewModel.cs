using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public sealed partial class KeyValueRowViewModel(KeyValueListViewModel owner) : ObservableObject
{
    [ObservableProperty] public partial bool Enabled { get; set; } = true;
    [ObservableProperty] public partial string Key { get; set; } = "";
    [ObservableProperty] public partial string Value { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial bool IsFile { get; set; }

    /// <summary>The always-present empty row at the bottom; typing into it creates a real row.</summary>
    [ObservableProperty] public partial bool IsPlaceholder { get; set; }

    public KeyValueListViewModel Owner => owner;

    public bool CanRemove => !IsPlaceholder && !owner.KeysReadOnly;

    /// <summary>Path variables are always sent, so they have no enabled checkbox.</summary>
    public bool ShowCheckBox => !IsPlaceholder && !owner.KeysReadOnly;

    partial void OnIsPlaceholderChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(ShowCheckBox));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Enabled) or nameof(Key) or nameof(Value) or nameof(Description) or nameof(IsFile))
            owner.OnRowChanged(this);
    }

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    [RelayCommand]
    private async Task PickFile()
    {
        if (owner.PickFile is { } pick && await pick() is { } path)
            Value = path;
    }

    public KeyValueItem ToModel() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = Value,
        Description = string.IsNullOrEmpty(Description) ? null : Description,
        Kind = IsFile ? KeyValueKind.File : KeyValueKind.Text,
    };
}
