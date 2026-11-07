using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Headers;
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

    /// <summary>
    /// A note shown after the key (for example "Overrides Acme"); empty for none, and then it takes no space. It is
    /// set from outside and isn't part of the row's data, so it never marks the table as changed.
    /// </summary>
    [ObservableProperty] public partial string TagText { get; set; } = "";

    /// <summary>A second note, shown as a warning with an icon.</summary>
    [ObservableProperty] public partial string WarningTagText { get; set; } = "";

    /// <summary>
    /// Masking, reveal and copy of the value. The row is sensitive when its list masks sensitive keys (headers) and
    /// its key is one of them.
    /// </summary>
    public SensitiveValueViewModel Sensitive { get; } = CreateSensitiveValue(owner);

    public bool HasTag => TagText.Length > 0;
    public bool HasWarningTag => WarningTagText.Length > 0;

    partial void OnTagTextChanged(string value) => OnPropertyChanged(nameof(HasTag));

    partial void OnWarningTagTextChanged(string value) => OnPropertyChanged(nameof(HasWarningTag));

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
        if (e.PropertyName is nameof(Key) or nameof(Value))
            SyncSensitiveValue();
        if (e.PropertyName is nameof(Enabled) or nameof(Key) or nameof(Value) or nameof(Description) or nameof(IsFile))
            owner.OnRowChanged(this);
    }

    private static SensitiveValueViewModel CreateSensitiveValue(KeyValueListViewModel owner) =>
        new("", isSensitive: false, text => owner.CopyText?.Invoke(text) ?? Task.CompletedTask);

    private void SyncSensitiveValue()
    {
        Sensitive.Value = Value;
        Sensitive.IsSensitive = owner.MasksSensitiveKeys && SensitiveHeaders.IsSensitiveName(Key);
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
