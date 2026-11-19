namespace Hermes.App.Controls;

/// <summary>What a <see cref="DropPriorityPanel"/> does to fit its width.</summary>
/// <param name="Step">How many steps of dropping are taken: 0 shows everything.</param>
/// <param name="ShrunkItemIndex">The index of the child squeezed to make the rest fit, or -1 when none is.</param>
/// <param name="ShrinkAmount">How much narrower than its natural width the squeezed child gets.</param>
public sealed record DropPlan(int Step, int ShrunkItemIndex, double ShrinkAmount);
