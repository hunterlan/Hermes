using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>App settings, stored in the config directory rather than the data directory because they point at it.</summary>
public static class SettingsStore
{
    public static AppSettings Load()
    {
        try
        {
            return JsonFileStore.Read(AppPaths.SettingsFile, HermesJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) =>
        JsonFileStore.Write(AppPaths.SettingsFile, settings, HermesJsonContext.Default.AppSettings);
}
