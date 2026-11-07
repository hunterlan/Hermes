using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Hermes.App.Controls;

/// <summary>
/// A small coloured tag with an icon and a text. The icon keeps the meaning readable without relying on colour alone.
/// The look for each <see cref="IconTagKind"/> lives in the control theme (Themes/Styles/IconTag.axaml).
/// </summary>
public sealed class IconTag : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<IconTag, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<IconTag, string?>(nameof(Text));

    public static readonly StyledProperty<IconTagKind> KindProperty =
        AvaloniaProperty.Register<IconTag, IconTagKind>(nameof(Kind), IconTagKind.Info);

    public IconTag()
    {
        UpdateKindPseudoClasses();
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IconTagKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
            UpdateKindPseudoClasses();
    }

    private void UpdateKindPseudoClasses()
    {
        PseudoClasses.Set(":ok", Kind == IconTagKind.Ok);
        PseudoClasses.Set(":info", Kind == IconTagKind.Info);
        PseudoClasses.Set(":warn", Kind == IconTagKind.Warn);
        PseudoClasses.Set(":err", Kind == IconTagKind.Err);
    }
}
