using System.Collections.ObjectModel;
using System.Text;
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

/// <summary>Editable table of key/value rows (params, headers, form fields, path variables).</summary>
public sealed partial class KeyValueListViewModel : ObservableObject
{
    private bool _loading;

    public KeyValueListViewModel(bool allowFiles = false, bool keysReadOnly = false)
    {
        AllowFiles = allowFiles;
        KeysReadOnly = keysReadOnly;
        if (!keysReadOnly)
            AddPlaceholder();
    }

    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];
    public bool AllowFiles { get; }
    public bool KeysReadOnly { get; }

    public Func<Task<string?>>? PickFile { get; set; }

    [ObservableProperty] public partial bool IsBulkEdit { get; set; }
    [ObservableProperty] public partial string BulkText { get; set; } = "";

    /// <summary>Number of enabled, non-empty rows (shown as a badge on the tab header).</summary>
    public int ActiveCount => Rows.Count(r => !r.IsPlaceholder && r.Enabled && r.Key.Length > 0);

    public string CountBadge => ActiveCount > 0 ? $" ({ActiveCount})" : "";

    public event Action? Changed;

    public void Load(IEnumerable<KeyValueItem> items)
    {
        _loading = true;
        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(new KeyValueRowViewModel(this)
            {
                Enabled = item.Enabled,
                Key = item.Key,
                Value = item.Value,
                Description = item.Description ?? "",
                IsFile = item.Kind == KeyValueKind.File,
            });
        }
        if (!KeysReadOnly)
            AddPlaceholder();
        _loading = false;
        RaiseCounts();
    }

    public List<KeyValueItem> ToModels() => Rows.Where(r => !r.IsPlaceholder).Select(r => r.ToModel()).ToList();

    internal void OnRowChanged(KeyValueRowViewModel row)
    {
        if (_loading)
            return;
        if (row.IsPlaceholder && (row.Key.Length > 0 || row.Value.Length > 0 || row.Description.Length > 0))
        {
            row.IsPlaceholder = false;
            AddPlaceholder();
        }
        RaiseCounts();
        Changed?.Invoke();
    }

    internal void Remove(KeyValueRowViewModel row)
    {
        if (row.IsPlaceholder)
            return;
        Rows.Remove(row);
        RaiseCounts();
        Changed?.Invoke();
    }

    private void AddPlaceholder()
    {
        var wasLoading = _loading;
        _loading = true;
        Rows.Add(new KeyValueRowViewModel(this) { IsPlaceholder = true, Enabled = true });
        _loading = wasLoading;
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CountBadge));
    }

    // ---- bulk edit: one "key:value" per line, "//" prefix = disabled ----

    partial void OnIsBulkEditChanged(bool value)
    {
        if (value)
        {
            var sb = new StringBuilder();
            foreach (var row in Rows.Where(r => !r.IsPlaceholder))
                sb.Append(row.Enabled ? "" : "//").Append(row.Key).Append(':').Append(row.Value).Append('\n');
            BulkText = sb.ToString();
        }
        else
        {
            ApplyBulkText(ParseBulkText(BulkText));
            Changed?.Invoke();
        }
    }

    private static List<KeyValueItem> ParseBulkText(string text)
    {
        var items = new List<KeyValueItem>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0)
                continue;
            var enabled = !line.StartsWith("//", StringComparison.Ordinal);
            if (!enabled)
                line = line[2..];
            var colon = line.IndexOf(':');
            items.Add(colon < 0
                ? new KeyValueItem(line.Trim(), "", enabled)
                : new KeyValueItem(line[..colon].Trim(), line[(colon + 1)..].TrimStart(), enabled));
        }
        return items;
    }

    /// <summary>
    /// Lines are matched to rows by key, in order (the second "a" line to the second "a" row), so a row keeps what
    /// the text doesn't show: its description and whether it's a file.
    /// </summary>
    private void ApplyBulkText(List<KeyValueItem> lines)
    {
        var rowsByKey = Rows.Where(r => !r.IsPlaceholder).GroupBy(r => r.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<KeyValueRowViewModel>(g), StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (rowsByKey.GetValueOrDefault(line.Key) is { Count: > 0 } rows && rows.Dequeue().ToModel() is var row)
            {
                line.Description = row.Description;
                line.Kind = row.Kind;
            }
        }
        Load(lines);
    }

    [RelayCommand]
    private void ToggleBulkEdit() => IsBulkEdit = !IsBulkEdit;
}
