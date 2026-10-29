using Avalonia.Data.Converters;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public static class UiConverters
{
    /// <summary>Dims the previous response while a new request is in flight.</summary>
    public static readonly IValueConverter LoadingOpacity = new FuncValueConverter<bool, double>(loading => loading ? 0.35 : 1.0);

    /// <summary>Masks secret values in text boxes.</summary>
    public static readonly IValueConverter SecretChar = new FuncValueConverter<bool, char>(secret => secret ? '•' : '\0');

    /// <summary>Upper-cases caption text (the design's text-transform: uppercase).</summary>
    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string>(text => text?.ToUpperInvariant() ?? "");

    /// <summary>Spoken and tooltip name of an environment colour swatch ("No color", "Red", ...).</summary>
    public static readonly IValueConverter EnvironmentColorName =
        new FuncValueConverter<EnvironmentColor, string>(color => color == EnvironmentColor.None ? "No color" : color.ToString());
}
