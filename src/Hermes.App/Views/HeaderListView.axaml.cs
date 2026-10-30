using Avalonia;
using Avalonia.Controls;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class HeaderListView : UserControl
{
    public static readonly StyledProperty<IEnumerable<HeaderRowViewModel>?> RowsProperty =
        AvaloniaProperty.Register<HeaderListView, IEnumerable<HeaderRowViewModel>?>(nameof(Rows));

    public HeaderListView() => InitializeComponent();

    public IEnumerable<HeaderRowViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }
}
