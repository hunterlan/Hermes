using Avalonia;
using Avalonia.Controls;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

public partial class HeaderListView : UserControl
{
    public static readonly StyledProperty<IEnumerable<HeaderRowViewModel>?> RowsProperty =
        AvaloniaProperty.Register<HeaderListView, IEnumerable<HeaderRowViewModel>?>(nameof(Rows));

    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<HeaderListView, string?>(nameof(Heading));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<HeaderListView, string?>(nameof(Hint));

    public static readonly DirectProperty<HeaderListView, bool> HasHeadingProperty =
        AvaloniaProperty.RegisterDirect<HeaderListView, bool>(nameof(HasHeading), view => view.HasHeading);

    public HeaderListView() => InitializeComponent();

    public IEnumerable<HeaderRowViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>A heading above the rows; none by default, as for the plain automatic rows.</summary>
    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>A short hint next to the heading.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public bool HasHeading => !string.IsNullOrEmpty(Heading);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeadingProperty)
            RaisePropertyChanged(HasHeadingProperty, !string.IsNullOrEmpty((string?)change.OldValue), HasHeading);
    }
}
