using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.ViewModels;

public sealed partial class SettingsDialogViewModel : ObservableObject
{
    /// <summary>About 100 years; far beyond any real need, and keeps the day arithmetic in range.</summary>
    public const int MaxHistoryRetentionDays = 36500;

    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly HttpClientProvider _http;
    private readonly string _currentDataDirectory;
    private readonly Func<Task<bool>> _closeTabsForTurningTabsOff;

    /// <param name="closeTabsForTurningTabsOff">
    /// Run before saving with Tabs turned off, to close the other tabs; false (the user cancelled) keeps Tabs on and the dialog open.
    /// </param>
    public SettingsDialogViewModel(SettingsService settings, DialogService dialogs, HttpClientProvider http, string currentDataDirectory,
        Func<Task<bool>> closeTabsForTurningTabsOff)
    {
        _settings = settings;
        _dialogs = dialogs;
        _http = http;
        _currentDataDirectory = currentDataDirectory;
        _closeTabsForTurningTabsOff = closeTabsForTurningTabsOff;

        var s = settings.Current;
        SelectedTheme = Themes.First(t => t.Value == s.Theme);
        SelectedLayout = Layouts.First(l => l.Value == s.Layout);
        TimeoutText = s.RequestTimeoutMs.ToString();
        VerifySsl = s.VerifySsl;
        FollowRedirects = s.FollowRedirects;
        MaxPrettySizeText = s.MaxPrettyResponseSizeMb.ToString();
        HistoryRetentionText = s.HistoryRetentionDays.ToString();
        ShowTabs = s.ShowTabs;
        DataDirectory = string.IsNullOrWhiteSpace(s.DataDirectory) ? AppPaths.DefaultDataDirectory : s.DataDirectory;
    }

    public IReadOnlyList<Option<ThemePreference>> Themes { get; } =
        [new("Dark", ThemePreference.Dark), new("Light", ThemePreference.Light), new("Sync with OS", ThemePreference.System)];

    public IReadOnlyList<Option<ResponseLayout>> Layouts { get; } =
        [new("Response below request", ResponseLayout.Vertical), new("Response beside request", ResponseLayout.Horizontal)];

    [ObservableProperty] public partial Option<ThemePreference> SelectedTheme { get; set; }
    [ObservableProperty] public partial Option<ResponseLayout> SelectedLayout { get; set; }
    [ObservableProperty] public partial string TimeoutText { get; set; }
    [ObservableProperty] public partial bool VerifySsl { get; set; }
    [ObservableProperty] public partial bool FollowRedirects { get; set; }
    [ObservableProperty] public partial string MaxPrettySizeText { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryRetentionError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string HistoryRetentionText { get; set; }
    [ObservableProperty] public partial string DataDirectory { get; set; }
    [ObservableProperty] public partial bool ShowTabs { get; set; }

    /// <summary>Set when the dialog was closed to open the manual, so the caller can open it.</summary>
    public bool ManualRequested { get; private set; }
    [ObservableProperty] public partial string CookieStatus { get; set; } = "";

    /// <summary>Why <see cref="HistoryRetentionText"/> can't be saved, or null when it can.</summary>
    public string? HistoryRetentionError => ValidateHistoryRetention(HistoryRetentionText, out _);

    public bool DataDirectoryChanged => !string.Equals(Path.GetFullPath(DataDirectory), Path.GetFullPath(_currentDataDirectory), StringComparison.Ordinal);

    partial void OnDataDirectoryChanged(string value) => OnPropertyChanged(nameof(DataDirectoryChanged));

    public event Action<bool>? CloseRequested;

    [RelayCommand]
    private async Task BrowseDataDirectory()
    {
        if (await _dialogs.PickFolderAsync("Choose data folder") is { } folder)
            DataDirectory = folder;
    }

    [RelayCommand]
    private void ClearCookies()
    {
        _http.ClearCookies();
        CookieStatus = "All cookies cleared.";
    }

    private bool CanSave() => HistoryRetentionError is null;

    private static string? ValidateHistoryRetention(string text, out int days)
    {
        days = 0;
        if (string.IsNullOrWhiteSpace(text))
            return "Enter a number of days. 0 keeps history forever.";
        if (!int.TryParse(text, out days))
            return "Enter a whole number of days.";
        if (days < 0)
            return "Days can't be negative.";
        return days > MaxHistoryRetentionDays ? $"Enter at most {MaxHistoryRetentionDays} days. 0 keeps history forever." : null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        static int Parse(string text, int fallback, int min) => int.TryParse(text, out var v) && v >= min ? v : fallback;

        if (ValidateHistoryRetention(HistoryRetentionText, out var historyRetentionDays) is not null)
            return;

        // Turning tabs off first closes the other tabs, which the user can cancel; nothing is saved then.
        if (_settings.Current.ShowTabs && !ShowTabs && !await _closeTabsForTurningTabsOff())
        {
            ShowTabs = true;
            return;
        }

        var current = _settings.Current;
        _settings.Update(new AppSettings
        {
            Theme = SelectedTheme.Value,
            Layout = SelectedLayout.Value,
            RequestTimeoutMs = Parse(TimeoutText, current.RequestTimeoutMs, 0),
            VerifySsl = VerifySsl,
            FollowRedirects = FollowRedirects,
            MaxPrettyResponseSizeMb = Parse(MaxPrettySizeText, current.MaxPrettyResponseSizeMb, 1),
            HistoryRetentionDays = historyRetentionDays,
            ShowTabs = ShowTabs,
            DataDirectory = DataDirectory == AppPaths.DefaultDataDirectory ? null : DataDirectory,
        });
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void OpenManual()
    {
        ManualRequested = true;
        CloseRequested?.Invoke(false);
    }
}
