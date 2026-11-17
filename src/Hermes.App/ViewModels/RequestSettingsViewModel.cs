using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>
/// The request's own settings. Where the request has no override, the toggles show the app setting and follow it.
/// </summary>
public sealed partial class RequestSettingsViewModel : ObservableObject
{
    private readonly SettingsService _appSettings;
    private bool _loading;

    /// <param name="appSettings">The app-wide settings the effective values fall back to.</param>
    public RequestSettingsViewModel(SettingsService appSettings)
    {
        _appSettings = appSettings;
    }

    /// <summary>Raised when the user changes a setting; not while it is being loaded.</summary>
    public event Action? Changed;

    /// <summary>The request's own setting; null follows the app setting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowRedirects))]
    [NotifyPropertyChangedFor(nameof(HasSettingsOverrides))]
    public partial bool? FollowRedirectsOverride { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifySsl))]
    [NotifyPropertyChangedFor(nameof(HasSettingsOverrides))]
    public partial bool? VerifySslOverride { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingsOverrides))]
    public partial string TimeoutText { get; set; } = "";

    /// <summary>The effective setting, shown by the toggle; turning the toggle makes it an override.</summary>
    public bool FollowRedirects
    {
        get => FollowRedirectsOverride ?? _appSettings.Current.FollowRedirects;
        set => FollowRedirectsOverride = value;
    }

    public bool VerifySsl
    {
        get => VerifySslOverride ?? _appSettings.Current.VerifySsl;
        set => VerifySslOverride = value;
    }

    public bool HasSettingsOverrides => FollowRedirectsOverride is not null || VerifySslOverride is not null || TimeoutMs is not null;

    private int? TimeoutMs => int.TryParse(TimeoutText, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var t) && t >= 0 ? t : null;

    /// <summary>Replaces the settings with <paramref name="settings"/> without raising <see cref="Changed"/>.</summary>
    public void Load(RequestSettings settings)
    {
        _loading = true;
        FollowRedirectsOverride = settings.FollowRedirects;
        VerifySslOverride = settings.VerifySsl;
        TimeoutText = settings.TimeoutMs?.ToString(CultureInfo.InvariantCulture) ?? "";
        _loading = false;
    }

    public RequestSettings ToModel() => new()
    {
        FollowRedirects = FollowRedirectsOverride,
        VerifySsl = VerifySslOverride,
        TimeoutMs = TimeoutMs,
    };

    /// <summary>The app settings changed: the toggles that follow them show the new value.</summary>
    public void OnAppSettingsChanged()
    {
        OnPropertyChanged(nameof(FollowRedirects));
        OnPropertyChanged(nameof(VerifySsl));
    }

    partial void OnFollowRedirectsOverrideChanged(bool? value) => RaiseChanged();

    partial void OnVerifySslOverrideChanged(bool? value) => RaiseChanged();

    partial void OnTimeoutTextChanged(string value) => RaiseChanged();

    private void RaiseChanged()
    {
        if (!_loading)
            Changed?.Invoke();
    }

    /// <summary>Drops the request's own settings, so it follows the app settings again.</summary>
    [RelayCommand]
    private void UseDefaultSettings()
    {
        FollowRedirectsOverride = null;
        VerifySslOverride = null;
        TimeoutText = "";
    }
}
