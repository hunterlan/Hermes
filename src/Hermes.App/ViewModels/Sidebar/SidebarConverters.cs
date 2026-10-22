using Avalonia.Data.Converters;

namespace Hermes.App.ViewModels.Sidebar;

public static class SidebarConverters
{
    /// <summary>The HTTP method of a request node, null for collections/folders.</summary>
    public static readonly IValueConverter MethodOf = new FuncValueConverter<object?, string?>(
        node => (node as RequestNodeViewModel)?.Method);
}
