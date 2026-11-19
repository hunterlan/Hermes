using Hermes.App.Controls;

namespace Hermes.App.Tests;

public class DropPlannerTests
{
    private static DropPlanItem Fixed(double width) => new(0, 0, width, 0);

    private static DropPlanItem DroppedFromStep(int step, double width, double shrinkMinimum = 0) => new(step, 0, width, shrinkMinimum);

    [Fact]
    public void Everything_fits_takes_no_step()
    {
        var plan = DropPlanner.Plan([Fixed(100), DroppedFromStep(1, 50), DroppedFromStep(2, 50)], 200);

        Assert.Equal(new DropPlan(0, -1, 0), plan);
    }

    [Fact]
    public void Takes_the_fewest_steps_that_fit()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 50), DroppedFromStep(2, 50), DroppedFromStep(3, 50)];

        Assert.Equal(0, DropPlanner.Plan(items, 250).Step);
        Assert.Equal(1, DropPlanner.Plan(items, 249).Step);
        Assert.Equal(1, DropPlanner.Plan(items, 200).Step);
        Assert.Equal(2, DropPlanner.Plan(items, 199).Step);
        Assert.Equal(3, DropPlanner.Plan(items, 100).Step);
    }

    [Fact]
    public void Nothing_fits_takes_the_last_step()
    {
        var plan = DropPlanner.Plan([Fixed(100), DroppedFromStep(1, 50)], 40);

        Assert.Equal(new DropPlan(1, -1, 0), plan);
    }

    [Fact]
    public void An_item_shown_from_a_step_replaces_the_one_dropped_at_that_step()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 80), new DropPlanItem(0, 1, 30, 0)];

        Assert.Equal(0, DropPlanner.Plan(items, 180).Step);
        // Dropping the wide item takes its compact replacement in.
        Assert.Equal(1, DropPlanner.Plan(items, 179).Step);
        Assert.Equal(1, DropPlanner.Plan(items, 130).Step);
    }

    [Fact]
    public void A_squeezable_item_is_squeezed_before_its_step_is_taken()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 80, shrinkMinimum: 30)];

        var plan = DropPlanner.Plan(items, 140);

        Assert.Equal(new DropPlan(0, 1, 40), plan);
    }

    [Fact]
    public void A_squeezable_item_is_dropped_once_it_would_be_squeezed_below_its_minimum()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 80, shrinkMinimum: 30)];

        Assert.Equal(new DropPlan(0, 1, 50), DropPlanner.Plan(items, 130));
        Assert.Equal(new DropPlan(1, -1, 0), DropPlanner.Plan(items, 129));
    }

    [Fact]
    public void An_item_with_nothing_to_show_takes_no_room()
    {
        var plan = DropPlanner.Plan([Fixed(100), DroppedFromStep(1, 0)], 100);

        Assert.Equal(0, plan.Step);
    }

    [Fact]
    public void The_same_width_gives_the_same_plan_whichever_way_it_was_reached()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 50), DroppedFromStep(2, 50)];
        var plansGoingDown = new[] { 300, 160, 120, 90 }.Select(width => DropPlanner.Plan(items, width)).ToList();

        var plansComingBack = new[] { 90, 120, 160, 300 }.Select(width => DropPlanner.Plan(items, width)).Reverse().ToList();

        Assert.Equal(plansGoingDown, plansComingBack);
    }

    [Fact]
    public void The_space_between_the_groups_counts_toward_the_width_needed()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 50), new DropPlanItem(0, 0, 40, 0, IsEndAligned: true)];

        // 198 is the 100 + 50 + 40 of the items plus the 8 between the groups; one less drops an item.
        Assert.Equal(0, DropPlanner.Plan(items, 198, 8).Step);
        Assert.Equal(1, DropPlanner.Plan(items, 197, 8).Step);
        // Without spacing the items alone fit in 190.
        Assert.Equal(0, DropPlanner.Plan(items, 190).Step);
    }

    [Fact]
    public void No_space_is_needed_when_a_group_has_nothing_to_show()
    {
        DropPlanItem[] onlyStart = [Fixed(100), DroppedFromStep(1, 0)];
        DropPlanItem[] onlyEnd = [new DropPlanItem(0, 0, 100, 0, IsEndAligned: true)];

        Assert.Equal(0, DropPlanner.Plan(onlyStart, 100, 8).Step);
        Assert.Equal(0, DropPlanner.Plan(onlyEnd, 100, 8).Step);
    }

    [Fact]
    public void The_space_between_the_groups_is_kept_when_an_item_is_squeezed()
    {
        DropPlanItem[] items = [Fixed(100), DroppedFromStep(1, 80, shrinkMinimum: 30), new DropPlanItem(0, 0, 40, 0, IsEndAligned: true)];

        var plan = DropPlanner.Plan(items, 200, 8);

        // 100 + 80 + 40 + 8 = 228 wanted in 200.
        Assert.Equal(new DropPlan(0, 1, 28), plan);
    }

    [Fact]
    public void An_empty_panel_takes_no_step()
    {
        Assert.Equal(new DropPlan(0, -1, 0), DropPlanner.Plan([], 100));
    }
}
