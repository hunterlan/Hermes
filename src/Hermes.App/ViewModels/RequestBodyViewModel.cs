using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Formatting;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>The body of a request: its mode, the raw text, the form tables and the binary file.</summary>
public sealed partial class RequestBodyViewModel : ObservableObject
{
    private const string FilePickerTitle = "Select file";

    private readonly DialogService _dialogs;
    private bool _loading;

    // Preserved, not editable in the UI.
    private string? _unsupportedBodyJson;

    public static IReadOnlyList<Option<BodyMode>> BodyModes { get; } =
    [
        new("none", BodyMode.None),
        new("form-data", BodyMode.FormData),
        new("x-www-form-urlencoded", BodyMode.UrlEncoded),
        new("raw", BodyMode.Raw),
        new("binary", BodyMode.Binary),
    ];

    public static IReadOnlyList<Option<RawLanguage>> RawLanguages { get; } =
    [
        new("Text", RawLanguage.Text),
        new("JavaScript", RawLanguage.JavaScript),
        new("JSON", RawLanguage.Json),
        new("HTML", RawLanguage.Html),
        new("XML", RawLanguage.Xml),
    ];

    /// <param name="dialogs">Opens the file picker for form-data files and the binary body.</param>
    public RequestBodyViewModel(DialogService dialogs)
    {
        _dialogs = dialogs;
        FormData.Changed += RaiseChanged;
        UrlEncoded.Changed += RaiseChanged;
        FormData.PickFile = () => dialogs.PickFileAsync(FilePickerTitle);
    }

    /// <summary>Raised when the user changes anything in the body; not while it is being loaded.</summary>
    public event Action? Changed;

    public KeyValueListViewModel FormData { get; } = new(allowFiles: true);
    public KeyValueListViewModel UrlEncoded { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    [NotifyPropertyChangedFor(nameof(IsBodyNone))]
    [NotifyPropertyChangedFor(nameof(IsBodyFormData))]
    [NotifyPropertyChangedFor(nameof(IsBodyUrlEncoded))]
    [NotifyPropertyChangedFor(nameof(IsBodyRaw))]
    [NotifyPropertyChangedFor(nameof(IsBodyBinary))]
    [NotifyPropertyChangedFor(nameof(HasUnsupportedBody))]
    public partial Option<BodyMode> SelectedBodyMode { get; set; } = BodyModes[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(RawEditorLanguage))]
    public partial Option<RawLanguage> SelectedRawLanguage { get; set; } = RawLanguages[2];

    [ObservableProperty] public partial string RawBody { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BinaryFileLabel))]
    public partial string? BinaryFilePath { get; set; }

    public bool HasBody => SelectedBodyMode.Value != BodyMode.None;
    public bool IsBodyNone => SelectedBodyMode.Value == BodyMode.None;
    public bool IsBodyFormData => SelectedBodyMode.Value == BodyMode.FormData;
    public bool IsBodyUrlEncoded => SelectedBodyMode.Value == BodyMode.UrlEncoded;
    public bool IsBodyRaw => SelectedBodyMode.Value == BodyMode.Raw;
    public bool IsBodyBinary => SelectedBodyMode.Value == BodyMode.Binary;

    /// <summary>A body of a mode Hermes can't edit (e.g. GraphQL) is kept for export; shown only while the mode is "none".</summary>
    public bool HasUnsupportedBody => _unsupportedBodyJson is not null && IsBodyNone;

    /// <summary>What the Body section shows next to its title: the mode, or the language for a raw body.</summary>
    public string Summary => IsBodyRaw ? SelectedRawLanguage.Label : SelectedBodyMode.Label;

    public string RawEditorLanguage => SelectedRawLanguage.Value switch
    {
        RawLanguage.Json => "json",
        RawLanguage.Xml => "xml",
        RawLanguage.Html => "html",
        RawLanguage.JavaScript => "javascript",
        _ => "text",
    };

    public string BinaryFileLabel => string.IsNullOrEmpty(BinaryFilePath) ? "No file selected" : Path.GetFileName(BinaryFilePath);

    /// <summary>Replaces the body with <paramref name="body"/> without raising <see cref="Changed"/>.</summary>
    public void Load(RequestBody body)
    {
        _loading = true;
        SelectedBodyMode = BodyModes.First(mode => mode.Value == body.Mode);
        SelectedRawLanguage = RawLanguages.First(language => language.Value == body.RawLanguage);
        RawBody = body.Raw;
        FormData.Load(body.FormData);
        UrlEncoded.Load(body.UrlEncoded);
        BinaryFilePath = body.BinaryFilePath;
        _unsupportedBodyJson = body.UnsupportedJson;
        _loading = false;
        OnPropertyChanged(nameof(HasUnsupportedBody));
    }

    public RequestBody ToModel() => new()
    {
        Mode = SelectedBodyMode.Value,
        Raw = RawBody,
        RawLanguage = SelectedRawLanguage.Value,
        FormData = FormData.ToModels(),
        UrlEncoded = UrlEncoded.ToModels(),
        BinaryFilePath = BinaryFilePath,
        UnsupportedJson = _unsupportedBodyJson,
    };

    /// <summary>
    /// Puts <paramref name="picked"/> in place of the file that couldn't be read, but only where that exact path is
    /// written. When no field holds it (it came from a variable), nothing is overwritten.
    /// </summary>
    /// <returns>True when a field was replaced.</returns>
    public bool UseChosenBodyFile(string picked, string? unreadablePath)
    {
        var replaced = false;
        if (IsBodyBinary)
        {
            if (BinaryFilePath == unreadablePath)
            {
                BinaryFilePath = picked;
                replaced = true;
            }
        }
        else
        {
            foreach (var row in FormData.Rows.Where(row => row.IsFile && row.Value == unreadablePath).ToList())
            {
                row.Value = picked;
                replaced = true;
            }
        }
        return replaced;
    }

    partial void OnSelectedBodyModeChanged(Option<BodyMode> value) => RaiseChanged();

    partial void OnSelectedRawLanguageChanged(Option<RawLanguage> value) => RaiseChanged();

    partial void OnRawBodyChanged(string value) => RaiseChanged();

    partial void OnBinaryFilePathChanged(string? value) => RaiseChanged();

    private void RaiseChanged()
    {
        if (!_loading)
            Changed?.Invoke();
    }

    [RelayCommand]
    private void SetBodyMode(BodyMode mode) => SelectedBodyMode = BodyModes.First(m => m.Value == mode);

    [RelayCommand]
    private void Beautify()
    {
        switch (SelectedRawLanguage.Value)
        {
            case RawLanguage.Json:
                RawBody = JsonFormatter.Format(RawBody);
                break;
            case RawLanguage.Xml or RawLanguage.Html:
                if (XmlFormatter.TryFormat(RawBody, out var xml))
                    RawBody = xml;
                break;
        }
    }

    [RelayCommand]
    private async Task SelectBinaryFile()
    {
        if (await _dialogs.PickFileAsync(FilePickerTitle) is { } path)
            BinaryFilePath = path;
    }
}
