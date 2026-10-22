using Avalonia.Data.Converters;

namespace Hermes.App.ViewModels;

public static class UiConverters
{
    /// <summary>Dims the previous response while a new request is in flight.</summary>
    public static readonly IValueConverter LoadingOpacity = new FuncValueConverter<bool, double>(loading => loading ? 0.35 : 1.0);
}
