namespace Hermes.Core.Models;

public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;
    public ResponseLayout Layout { get; set; } = ResponseLayout.Vertical;

    /// <summary>0 = no timeout.</summary>
    public int RequestTimeoutMs { get; set; }
    public bool VerifySsl { get; set; } = true;
    public bool FollowRedirects { get; set; } = true;

    /// <summary>Responses bigger than this are shown raw, without highlighting.</summary>
    public int MaxPrettyResponseSizeMb { get; set; } = 5;

    /// <summary>Overrides the default data directory. Takes effect on restart.</summary>
    public string? DataDirectory { get; set; }

    public int HistoryRetentionDays { get; set; } = 30;

    /// <summary>When off, opening an item replaces the current clean tab and the strip only shows while unsaved tabs remain.</summary>
    public bool ShowTabs { get; set; } = true;
}
