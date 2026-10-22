using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Hermes.Core.Variables;

namespace Hermes.App.Views;

public partial class KeyValueTable : UserControl
{
    public static readonly StyledProperty<VariableScope?> VariablesProperty =
        AvaloniaProperty.Register<KeyValueTable, VariableScope?>(nameof(Variables));

    public static readonly IValueConverter BoolToIndex = new FuncValueConverter<bool, int>(b => b ? 1 : 0, i => i == 1);

    public KeyValueTable() => InitializeComponent();

    public VariableScope? Variables
    {
        get => GetValue(VariablesProperty);
        set => SetValue(VariablesProperty, value);
    }
}
