namespace Hermes.Core.Storage;

public static class AppPaths
{
    /// <summary>
    /// ~/.config/Hermes on Linux, %APPDATA%\Hermes on Windows, ~/Library/Application Support/Hermes on macOS.
    /// Overridable with the <c>HERMES_CONFIG_DIR</c> environment variable (used by tests and portable setups).
    /// </summary>
    public static string ConfigDirectory { get; } =
        Environment.GetEnvironmentVariable("HERMES_CONFIG_DIR") is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Hermes");

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public static string DefaultDataDirectory => ConfigDirectory;
}
