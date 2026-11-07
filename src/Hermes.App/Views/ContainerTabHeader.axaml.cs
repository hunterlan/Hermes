using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Hermes.App.Views;

/// <summary>
/// The header shared by collection, folder and environment tabs: icon, editable name and a Save button.
/// Extra buttons (such as "Set active") go in <see cref="Actions"/> and appear to the left of Save.
/// </summary>
public partial class ContainerTabHeader : UserControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<ContainerTabHeader, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ContainerTabHeader, string?>(nameof(Title), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>When false the name is shown as fixed text (<see cref="ReadOnlyTitle"/>) instead of a box.</summary>
    public static readonly StyledProperty<bool> IsTitleEditableProperty =
        AvaloniaProperty.Register<ContainerTabHeader, bool>(nameof(IsTitleEditable), defaultValue: true);

    public static readonly StyledProperty<string?> ReadOnlyTitleProperty =
        AvaloniaProperty.Register<ContainerTabHeader, string?>(nameof(ReadOnlyTitle));

    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<ContainerTabHeader, ICommand?>(nameof(SaveCommand));

    public ContainerTabHeader() => InitializeComponent();

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsTitleEditable
    {
        get => GetValue(IsTitleEditableProperty);
        set => SetValue(IsTitleEditableProperty, value);
    }

    public string? ReadOnlyTitle
    {
        get => GetValue(ReadOnlyTitleProperty);
        set => SetValue(ReadOnlyTitleProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    /// <summary>Extra buttons shown before Save. Hidden ones take no space.</summary>
    public Avalonia.Controls.Controls Actions => ActionsPanel.Children;
}
