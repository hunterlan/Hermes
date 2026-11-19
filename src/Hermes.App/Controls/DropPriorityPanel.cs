using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace Hermes.App.Controls;

/// <summary>A horizontal row that hides its least important children when it runs out of width.</summary>
public class DropPriorityPanel : Panel
{
    /// <summary>Step at which the child is hidden; 1 goes first, 0 never.</summary>
    public static readonly AttachedProperty<int> HiddenFromStepProperty =
        AvaloniaProperty.RegisterAttached<DropPriorityPanel, Control, int>("HiddenFromStep");

    /// <summary>Step from which the child is shown, e.g. a compact replacement.</summary>
    public static readonly AttachedProperty<int> ShownFromStepProperty =
        AvaloniaProperty.RegisterAttached<DropPriorityPanel, Control, int>("ShownFromStep");

    /// <summary>Width the child may be squeezed to before it is hidden; 0 means never squeezed.</summary>
    public static readonly AttachedProperty<double> ShrinkMinimumWidthProperty =
        AvaloniaProperty.RegisterAttached<DropPriorityPanel, Control, double>("ShrinkMinimumWidth");

    /// <summary>The child sits at the right edge.</summary>
    public static readonly AttachedProperty<bool> IsEndAlignedProperty =
        AvaloniaProperty.RegisterAttached<DropPriorityPanel, Control, bool>("IsEndAligned");

    /// <summary>Set by the panel while the child is hidden.</summary>
    public static readonly AttachedProperty<bool> IsDroppedProperty =
        AvaloniaProperty.RegisterAttached<DropPriorityPanel, Control, bool>("IsDropped");

    /// <summary>Minimum gap between the left and right groups.</summary>
    public static readonly StyledProperty<double> GroupSpacingProperty =
        AvaloniaProperty.Register<DropPriorityPanel, double>(nameof(GroupSpacing));

    private readonly ConditionalWeakTable<Control, DroppedChildInputState> _inputStatesBeforeDrop = new();
    private DropPlan _plan = new(0, -1, 0);

    static DropPriorityPanel()
    {
        ClipToBoundsProperty.OverrideDefaultValue<DropPriorityPanel>(true);
        AffectsParentMeasure<DropPriorityPanel>(HiddenFromStepProperty, ShownFromStepProperty, ShrinkMinimumWidthProperty);
        AffectsParentMeasure<DropPriorityPanel>(IsEndAlignedProperty);
        AffectsMeasure<DropPriorityPanel>(GroupSpacingProperty);
    }

    public double GroupSpacing
    {
        get => GetValue(GroupSpacingProperty);
        set => SetValue(GroupSpacingProperty, value);
    }

    /// <summary>Steps taken in the last layout; 0 shows everything.</summary>
    public int Step => _plan.Step;

    public static int GetHiddenFromStep(Control child) => child.GetValue(HiddenFromStepProperty);

    public static void SetHiddenFromStep(Control child, int value) => child.SetValue(HiddenFromStepProperty, value);

    public static int GetShownFromStep(Control child) => child.GetValue(ShownFromStepProperty);

    public static void SetShownFromStep(Control child, int value) => child.SetValue(ShownFromStepProperty, value);

    public static double GetShrinkMinimumWidth(Control child) => child.GetValue(ShrinkMinimumWidthProperty);

    public static void SetShrinkMinimumWidth(Control child, double value) => child.SetValue(ShrinkMinimumWidthProperty, value);

    public static bool GetIsEndAligned(Control child) => child.GetValue(IsEndAlignedProperty);

    public static void SetIsEndAligned(Control child, bool value) => child.SetValue(IsEndAlignedProperty, value);

    public static bool GetIsDropped(Control child) => child.GetValue(IsDroppedProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        var naturalSize = new Size(double.PositiveInfinity, availableSize.Height);
        var items = new List<DropPlanItem>(Children.Count);
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(naturalSize);
            height = Math.Max(height, child.DesiredSize.Height);
            items.Add(new DropPlanItem(GetHiddenFromStep(child), GetShownFromStep(child), child.DesiredSize.Width, GetShrinkMinimumWidth(child), GetIsEndAligned(child)));
        }

        _plan = DropPlanner.Plan(items, availableSize.Width, GroupSpacing);

        var width = 0.0;
        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            var isShown = items[index].IsShownAt(_plan.Step);
            SetDropped(child, !isShown);
            if (!isShown)
                continue;
            if (index == _plan.ShrunkItemIndex)
                child.Measure(new Size(items[index].NaturalWidth - _plan.ShrinkAmount, availableSize.Height));
            width += child.DesiredSize.Width;
        }
        var hasBothGroups = Children.Any(child => !GetIsDropped(child) && !GetIsEndAligned(child) && child.DesiredSize.Width > 0)
            && Children.Any(child => !GetIsDropped(child) && GetIsEndAligned(child) && child.DesiredSize.Width > 0);
        if (hasBothGroups)
            width += GroupSpacing;
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(child => !GetIsDropped(child)).ToList();
        var startX = 0.0;
        foreach (var child in shown.Where(child => !GetIsEndAligned(child)))
        {
            child.Arrange(new Rect(startX, 0, child.DesiredSize.Width, finalSize.Height));
            startX += child.DesiredSize.Width;
        }

        var endWidth = shown.Where(GetIsEndAligned).Sum(child => child.DesiredSize.Width);
        var gap = startX > 0 && endWidth > 0 ? GroupSpacing : 0;
        var endX = Math.Max(startX + gap, finalSize.Width - endWidth);
        foreach (var child in shown.Where(GetIsEndAligned))
        {
            child.Arrange(new Rect(endX, 0, child.DesiredSize.Width, finalSize.Height));
            endX += child.DesiredSize.Width;
        }

        // Past the clipped right edge: a zero-size rect can't shrink a child with a fixed size.
        foreach (var child in Children.Where(GetIsDropped))
            child.Arrange(new Rect(finalSize.Width, 0, child.DesiredSize.Width, finalSize.Height));
        return finalSize;
    }

    /// <summary>A hidden child can't be clicked or tabbed to.</summary>
    private void SetDropped(Control child, bool isDropped)
    {
        child.SetValue(IsDroppedProperty, isDropped);
        if (isDropped && !_inputStatesBeforeDrop.TryGetValue(child, out _))
        {
            _inputStatesBeforeDrop.Add(child, new DroppedChildInputState(child.IsTabStop, child.IsHitTestVisible));
            child.SetCurrentValue(IsTabStopProperty, false);
            child.SetCurrentValue(IsHitTestVisibleProperty, false);
        }
        else if (!isDropped && _inputStatesBeforeDrop.TryGetValue(child, out var before))
        {
            _inputStatesBeforeDrop.Remove(child);
            child.SetCurrentValue(IsTabStopProperty, before.IsTabStop);
            child.SetCurrentValue(IsHitTestVisibleProperty, before.IsHitTestVisible);
        }
    }
}
