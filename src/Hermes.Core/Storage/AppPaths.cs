namespace Hermes.Core.Storage;

public static class AppPaths
{
    /// <summary>~/.config/Hermes on Linux, %APPDATA%\Hermes on Windows, ~/Library/Application Support/Hermes on macOS.</summary>
    public static string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Hermes");

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public static string DefaultDataDirectory => ConfigDirectory;
}
