using System.Globalization;
using Avalonia.Data.Converters;

namespace Hermes.App.Converters;

/// <summary>Formats an int index into a "selected" bool for segmented buttons.</summary>
public sealed class IndexEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter?.ToString(), out var p) ? p : Avalonia.Data.BindingOperations.DoNothing;
}
