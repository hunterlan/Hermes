using Avalonia.Data.Converters;

namespace Hermes.App.Converters;

public static class Converters
{
    /// <summary>True when the bound value equals the converter parameter (compared as strings). For radio-like toggles.</summary>
    public static readonly IValueConverter EqualsParameter = new FuncValueConverter<object?, object?, bool>(
        (value, parameter) => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal));

    public static readonly IValueConverter IsNotNullOrEmpty = new FuncValueConverter<object?, bool>(
        value => value is string s ? s.Length > 0 : value is not null);
}
