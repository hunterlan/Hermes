using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hermes.Core.Models;

namespace Hermes.App.Controls;

/// <summary>
/// An environment's swatch: a circle in the current theme's shade of its colour, or a hollow ring when it has none.
/// Decoration only; the active checkmark and the confirm-before-send lock carry the status.
/// </summary>
public sealed class EnvironmentColorDot : Control
{
    public static readonly StyledProperty<EnvironmentColor> ColorProperty =
        AvaloniaProperty.Register<EnvironmentColorDot, EnvironmentColor>(nameof(Color));

    /// <summary>Draw "no colour" as a dashed ring (the editor's swatch) rather than a solid one.</summary>
    public static readonly StyledProperty<bool> IsNoneRingDashedProperty =
        AvaloniaProperty.Register<EnvironmentColorDot, bool>(nameof(IsNoneRingDashed));

    private static readonly StyledProperty<IBrush?> ResolvedBrushProperty =
        AvaloniaProperty.Register<EnvironmentColorDot, IBrush?>(nameof(ResolvedBrush));

    private const double RingThickness = 1.5;
    private const string NoneRingBrushKey = "TextTertiaryBrush";

    // Dash and gap lengths are in multiples of the ring thickness.
    private static readonly DashStyle NoneRingDashes = new([2, 2], 0);

    private IDisposable? _brushBinding;

    // Defaults rather than local values, so styles can still size and align the dot.
    static EnvironmentColorDot()
    {
        WidthProperty.OverrideDefaultValue<EnvironmentColorDot>(8);
        HeightProperty.OverrideDefaultValue<EnvironmentColorDot>(8);
        VerticalAlignmentProperty.OverrideDefaultValue<EnvironmentColorDot>(Avalonia.Layout.VerticalAlignment.Center);
        AffectsRender<EnvironmentColorDot>(ColorProperty, IsNoneRingDashedProperty, ResolvedBrushProperty);
    }

    public EnvironmentColorDot() => BindBrush();

    public EnvironmentColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public bool IsNoneRingDashed
    {
        get => GetValue(IsNoneRingDashedProperty);
        set => SetValue(IsNoneRingDashedProperty, value);
    }

    /// <summary>The fill, or for no colour the ring, as resolved for the current theme.</summary>
    public IBrush? ResolvedBrush => GetValue(ResolvedBrushProperty);

    public override void Render(DrawingContext context)
    {
        if (ResolvedBrush is not { } brush)
            return;
        var bounds = new Rect(Bounds.Size);
        if (Color == EnvironmentColor.None)
        {
            var pen = new Pen(brush, RingThickness, IsNoneRingDashed ? NoneRingDashes : null);
            context.DrawEllipse(null, pen, bounds.Deflate(RingThickness / 2));
        }
        else
        {
            context.DrawEllipse(brush, null, bounds);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
            BindBrush();
    }

    // A resource observable re-resolves when the theme changes, so the shade follows a light/dark switch.
    private void BindBrush()
    {
        _brushBinding?.Dispose();
        _brushBinding = this.Bind(ResolvedBrushProperty, this.GetResourceObservable(BrushKeyFor(Color)));
    }

    private static string BrushKeyFor(EnvironmentColor color) =>
        color == EnvironmentColor.None ? NoneRingBrushKey : $"EnvironmentColor{color}Brush";
}
