using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public static class UiConverters
{
    /// <summary>Dims the previous response while a new request is in flight.</summary>
    public static readonly IValueConverter LoadingOpacity = new FuncValueConverter<bool, double>(loading => loading ? 0.35 : 1.0);

    /// <summary>Masks secret values in text boxes.</summary>
    public static readonly IValueConverter SecretChar = new FuncValueConverter<bool, char>(secret => secret ? '•' : '\0');

    /// <summary>Summary of the request Settings section: says so when something overrides the app defaults, else nothing.</summary>
    public static readonly IValueConverter OverridesSummary =
        new FuncValueConverter<bool, string>(hasOverrides => hasOverrides ? "Overrides app defaults" : "");

    /// <summary>Upper-cases caption text (the design's text-transform: uppercase).</summary>
    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string>(text => text?.ToUpperInvariant() ?? "");

    /// <summary>Looks up an icon geometry by its resource key (e.g. "IconEnvironment"); null when the key is null or unknown.</summary>
    public static readonly IValueConverter IconFromKey = new FuncValueConverter<string?, Geometry?>(key =>
    {
        if (key is null || Application.Current is not { } application)
        {
            return null;
        }

        return application.TryFindResource(key, application.ActualThemeVariant, out var resource) ? resource as Geometry : null;
    });

    /// <summary>Spoken and tooltip name of an environment colour swatch ("No color", "Red", ...).</summary>
    public static readonly IValueConverter EnvironmentColorName =
        new FuncValueConverter<EnvironmentColor, string>(color => color == EnvironmentColor.None ? "No color" : color.ToString());
}
