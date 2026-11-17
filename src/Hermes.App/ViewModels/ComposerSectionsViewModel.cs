using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermes.App.ViewModels;

/// <summary>
/// Which sections of the request composer are on screen and which are expanded. Only what the request uses is shown;
/// the rest waits as "Not used yet" chips.
/// </summary>
public sealed partial class ComposerSectionsViewModel : ObservableObject
{
    private readonly RequestUrlEditorViewModel _urlEditor;
    private readonly RequestBodyViewModel _body;
    private readonly RequestSettingsViewModel _settings;

    /// <param name="urlEditor">
    /// The URL with its params; the Params section shows while a param is active or the URL has path variables, and
    /// stays once the user edits the params.
    /// </param>
    /// <param name="body">The body; the section shows while it has content, and stays once the user changes its mode.</param>
    /// <param name="settings">The settings; the section shows while any is overridden, and stays once edited.</param>
    public ComposerSectionsViewModel(RequestUrlEditorViewModel urlEditor, RequestBodyViewModel body, RequestSettingsViewModel settings)
    {
        _urlEditor = urlEditor;
        _body = body;
        _settings = settings;
        _urlEditor.Params.PropertyChanged += OnQueryParamsPropertyChanged;
        _urlEditor.PropertyChanged += OnUrlEditorPropertyChanged;
        _urlEditor.ParamsEdited += OnParamsEdited;
        _body.PropertyChanged += OnBodyPropertyChanged;
        _body.ModeChanged += OnBodyModeChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _settings.Changed += OnSettingsEdited;
    }

    /// <summary>Raised when the Settings section should be brought on screen; the view scrolls to it.</summary>
    public event Action? SettingsRevealRequested;

    /// <summary>Raised when the Headers section should be brought on screen; the view scrolls to it.</summary>
    public event Action? HeadersRevealRequested;

    /// <summary>Raised when the Body section should be brought on screen; the view scrolls to it.</summary>
    public event Action? BodyRevealRequested;

    /// <summary>The request's HTTP method; POST, PUT and PATCH bring the Body section on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodySectionVisible))]
    [NotifyPropertyChangedFor(nameof(HasUnusedSections))]
    public partial string Method { get; set; } = "GET";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParamsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(HasUnusedSections))]
    public partial bool ParamsAdded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodySectionVisible))]
    [NotifyPropertyChangedFor(nameof(HasUnusedSections))]
    public partial bool BodyAdded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsSectionVisible))]
    [NotifyPropertyChangedFor(nameof(HasUnusedSections))]
    public partial bool SettingsAdded { get; set; }

    [ObservableProperty] public partial bool ParamsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool HeadersExpanded { get; set; } = true;
    [ObservableProperty] public partial bool AuthExpanded { get; set; }
    [ObservableProperty] public partial bool BodyExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsExpanded { get; set; } = true;

    public bool IsParamsSectionVisible => ParamsAdded || _urlEditor.Params.ActiveCount > 0 || _urlEditor.HasPathVariables;

    public bool IsBodySectionVisible =>
        BodyAdded || _body.HasBody || _body.HasUnsupportedBody || Method is "POST" or "PUT" or "PATCH";

    public bool IsSettingsSectionVisible => SettingsAdded || _settings.HasSettingsOverrides;

    public bool HasUnusedSections => !IsParamsSectionVisible || !IsBodySectionVisible || !IsSettingsSectionVisible;

    public string ParamsSummary => _urlEditor.Params.ActiveCount is var count and > 0 ? $"{count} active" : "";

    /// <summary>Shows the Settings section, expanded, and asks the view to bring it on screen.</summary>
    public void RevealSettings()
    {
        SettingsAdded = SettingsExpanded = true;
        SettingsRevealRequested?.Invoke();
    }

    public void RevealHeaders()
    {
        HeadersExpanded = true;
        HeadersRevealRequested?.Invoke();
    }

    /// <summary>Shows the Body section, expanded, and asks the view to bring it on screen.</summary>
    public void RevealBody()
    {
        BodyAdded = BodyExpanded = true;
        BodyRevealRequested?.Invoke();
    }

    /// <summary>A "Not used yet" chip: brings the section on screen, expanded.</summary>
    [RelayCommand]
    private void AddSection(string section)
    {
        switch (section)
        {
            case "params":
                ParamsAdded = ParamsExpanded = true;
                break;
            case "body":
                BodyAdded = BodyExpanded = true;
                break;
            case "settings":
                SettingsAdded = SettingsExpanded = true;
                break;
        }
    }

    private void OnQueryParamsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyValueListViewModel.ActiveCount))
        {
            OnPropertyChanged(nameof(IsParamsSectionVisible));
            OnPropertyChanged(nameof(HasUnusedSections));
            OnPropertyChanged(nameof(ParamsSummary));
        }
    }

    private void OnUrlEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestUrlEditorViewModel.HasPathVariables))
        {
            OnPropertyChanged(nameof(IsParamsSectionVisible));
            OnPropertyChanged(nameof(HasUnusedSections));
        }
    }

    private void OnBodyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestBodyViewModel.HasBody) or nameof(RequestBodyViewModel.HasUnsupportedBody))
        {
            OnPropertyChanged(nameof(IsBodySectionVisible));
            OnPropertyChanged(nameof(HasUnusedSections));
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestSettingsViewModel.HasSettingsOverrides))
        {
            OnPropertyChanged(nameof(IsSettingsSectionVisible));
            OnPropertyChanged(nameof(HasUnusedSections));
        }
    }

    /// <summary>A section the user edits stays on screen even if it becomes empty again; a load doesn't count.</summary>
    private void OnParamsEdited() => ParamsAdded = true;

    private void OnBodyModeChanged() => BodyAdded = true;

    private void OnSettingsEdited() => SettingsAdded = true;
}
