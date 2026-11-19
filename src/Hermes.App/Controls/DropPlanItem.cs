namespace Hermes.App.Controls;

/// <summary>One child of a <see cref="DropPriorityPanel"/> as the <see cref="DropPlanner"/> sees it.</summary>
/// <param name="HiddenFromStep">The step from which the child is dropped; 0 never drops it.</param>
/// <param name="ShownFromStep">The step from which the child is shown; 0 shows it from the first step.</param>
/// <param name="NaturalWidth">The width the child wants, margins included. 0 for a child that has nothing to show.</param>
/// <param name="ShrinkMinimumWidth">The least width the child can be squeezed to; 0 means it can't be squeezed.</param>
/// <param name="IsEndAligned">Whether the child is in the group at the right edge instead of the group from the left.</param>
public sealed record DropPlanItem(int HiddenFromStep, int ShownFromStep, double NaturalWidth, double ShrinkMinimumWidth, bool IsEndAligned = false)
{
    /// <summary>Whether the child is shown once the panel has taken <paramref name="step"/> steps.</summary>
    public bool IsShownAt(int step) =>
        step >= ShownFromStep && (HiddenFromStep == 0 || step < HiddenFromStep);
}
