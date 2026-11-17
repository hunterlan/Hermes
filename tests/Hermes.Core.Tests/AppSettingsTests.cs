using Hermes.Core.Models;

namespace Hermes.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void A_copy_has_the_same_values_and_changing_it_leaves_the_original_alone()
    {
        var original = new AppSettings
        {
            Theme = ThemePreference.Light,
            Layout = ResponseLayout.Horizontal,
            RequestTimeoutMs = 1500,
            VerifySsl = false,
            FollowRedirects = false,
            MaxPrettyResponseSizeMb = 9,
            DataDirectory = "/data/hermes",
            HistoryRetentionDays = 7,
            ShowTabs = false,
            TabViewPoolSize = 5,
        };

        var copy = original.Copy();
        copy.DataDirectory = null;
        copy.ShowTabs = true;

        Assert.NotSame(original, copy);
        Assert.Equal("/data/hermes", original.DataDirectory);
        Assert.False(original.ShowTabs);
        Assert.Equal(original.Theme, copy.Theme);
        Assert.Equal(original.Layout, copy.Layout);
        Assert.Equal(original.RequestTimeoutMs, copy.RequestTimeoutMs);
        Assert.Equal(original.VerifySsl, copy.VerifySsl);
        Assert.Equal(original.FollowRedirects, copy.FollowRedirects);
        Assert.Equal(original.MaxPrettyResponseSizeMb, copy.MaxPrettyResponseSizeMb);
        Assert.Equal(original.HistoryRetentionDays, copy.HistoryRetentionDays);
        Assert.Equal(original.TabViewPoolSize, copy.TabViewPoolSize);
    }

    [Fact]
    public void Every_setting_is_a_value_or_a_string_so_a_shallow_copy_is_a_full_copy()
    {
        var reference = typeof(AppSettings).GetProperties()
            .Where(property => !property.PropertyType.IsValueType && property.PropertyType != typeof(string))
            .Select(property => property.Name);

        Assert.Empty(reference);
    }
}
