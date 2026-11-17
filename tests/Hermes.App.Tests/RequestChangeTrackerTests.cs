using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class RequestChangeTrackerTests
{
    private const string Saved = """{"url":"https://example.com/a"}""";
    private const string Edited = """{"url":"https://example.com/b"}""";

    [Fact]
    public void The_saved_snapshot_is_clean()
    {
        var tracker = new RequestChangeTracker();

        tracker.MarkSaved(Saved);

        Assert.False(tracker.IsDirty(Saved));
    }

    [Fact]
    public void A_different_snapshot_is_dirty_and_the_saved_one_is_clean_again()
    {
        var tracker = new RequestChangeTracker();
        tracker.MarkSaved(Saved);

        Assert.True(tracker.IsDirty(Edited));
        Assert.False(tracker.IsDirty(Saved));
    }

    [Fact]
    public void Saving_again_makes_the_new_snapshot_the_clean_one()
    {
        var tracker = new RequestChangeTracker();
        tracker.MarkSaved(Saved);

        tracker.MarkSaved(Edited);

        Assert.False(tracker.IsDirty(Edited));
        Assert.True(tracker.IsDirty(Saved));
    }

    [Fact]
    public void Changes_count_as_edits_only_outside_a_loading_scope()
    {
        var tracker = new RequestChangeTracker();
        Assert.False(tracker.IsLoading);

        using (tracker.Loading())
            Assert.True(tracker.IsLoading);

        Assert.False(tracker.IsLoading);
    }

    [Fact]
    public void Loading_lasts_until_the_outermost_scope_ends()
    {
        var tracker = new RequestChangeTracker();

        using (tracker.Loading())
        {
            using (tracker.Loading())
                Assert.True(tracker.IsLoading);
            Assert.True(tracker.IsLoading);
        }

        Assert.False(tracker.IsLoading);
    }

    [Fact]
    public void Disposing_a_scope_twice_ends_it_once()
    {
        var tracker = new RequestChangeTracker();
        using var outer = tracker.Loading();
        var inner = tracker.Loading();

        inner.Dispose();
        inner.Dispose();

        Assert.True(tracker.IsLoading);
    }

    [Fact]
    public void A_load_that_throws_still_ends_its_scope()
    {
        var tracker = new RequestChangeTracker();

        void LoadThatFails()
        {
            using (tracker.Loading())
                throw new InvalidOperationException("The request couldn't be loaded.");
        }

        Assert.Throws<InvalidOperationException>(LoadThatFails);
        Assert.False(tracker.IsLoading);
    }
}
