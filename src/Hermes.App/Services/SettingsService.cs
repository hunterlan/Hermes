using Avalonia;
using Avalonia.Styling;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = SettingsStore.Load();

    public event Action? Changed;

    public void Update(AppSettings settings)
    {
        Current = settings;
        SettingsStore.Save(settings);
        ApplyTheme();
        Changed?.Invoke();
    }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Current.Theme switch
            {
                ThemePreference.Light => ThemeVariant.Light,
                ThemePreference.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    public void ToggleTheme()
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Current.Theme = isDark ? ThemePreference.Light : ThemePreference.Dark;
        Update(Current);
    }
}
