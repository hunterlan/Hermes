namespace Hermes.App.Controls;

/// <summary>
/// Decides how far a <see cref="DropPriorityPanel"/> has to drop items to fit. The answer depends only on the
/// available width and the children's natural widths, never on what is currently shown, so the same width always
/// gives the same layout: a pane resized across a threshold can't flicker between two states.
/// </summary>
public static class DropPlanner
{
    /// <summary>
    /// The fewest steps after which the shown items fit in <paramref name="availableWidth"/>. When one shown item
    /// can be squeezed and that is enough, it is squeezed instead of the step being taken. When nothing fits, the
    /// last step is used and the panel clips what is left.
    /// </summary>
    /// <param name="groupSpacing">The least space between the group from the left and the group at the right edge,
    /// counted whenever both groups have something to show.</param>
    public static DropPlan Plan(IReadOnlyList<DropPlanItem> items, double availableWidth, double groupSpacing = 0)
    {
        var lastStep = items.Count == 0 ? 0 : items.Max(item => Math.Max(item.HiddenFromStep, item.ShownFromStep));
        for (var step = 0; step <= lastStep; step++)
        {
            var overflow = RequiredWidth(items, step, groupSpacing) - availableWidth;
            if (overflow <= 0)
                return new DropPlan(step, -1, 0);

            var squeezable = FindSqueezableIndex(items, step, overflow);
            if (squeezable >= 0)
                return new DropPlan(step, squeezable, overflow);
        }
        return new DropPlan(lastStep, -1, 0);
    }

    private static double RequiredWidth(IReadOnlyList<DropPlanItem> items, int step, double groupSpacing)
    {
        var startWidth = items.Where(item => item.IsShownAt(step) && !item.IsEndAligned).Sum(item => item.NaturalWidth);
        var endWidth = items.Where(item => item.IsShownAt(step) && item.IsEndAligned).Sum(item => item.NaturalWidth);
        return startWidth + endWidth + (startWidth > 0 && endWidth > 0 ? groupSpacing : 0);
    }

    private static int FindSqueezableIndex(IReadOnlyList<DropPlanItem> items, int step, double overflow)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.IsShownAt(step) && item.ShrinkMinimumWidth > 0 && item.NaturalWidth - overflow >= item.ShrinkMinimumWidth)
                return index;
        }
        return -1;
    }
}
