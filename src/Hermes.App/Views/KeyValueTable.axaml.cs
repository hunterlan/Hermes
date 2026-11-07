using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Hermes.App.Views;

public partial class KeyValueTable : UserControl
{
    public static readonly IValueConverter BoolToIndex = new FuncValueConverter<bool, int>(b => b ? 1 : 0, i => i == 1);

    public KeyValueTable() => InitializeComponent();
}
