using Hermes.Core.Models;
using Hermes.Core.Web;

namespace Hermes.Core.Tests;

public class UrlUtilitiesTests
{
    [Fact]
    public void Parses_query_without_decoding()
    {
        var items = UrlUtilities.ParseQuery(UrlUtilities.Split("http://x/a?q=a%20b&flag&e=").Query);
        Assert.Equal(["q", "flag", "e"], items.Select(i => i.Key));
        Assert.Equal("a%20b", items[0].Value);
    }

    [Fact]
    public void WithQuery_skips_disabled_and_keeps_fragment()
    {
        var url = UrlUtilities.WithQuery("http://x/a?old=1#frag", [new KeyValueItem("a", "1"), new KeyValueItem("b", "2", enabled: false), new KeyValueItem("c", "")]);
        Assert.Equal("http://x/a?a=1&c#frag", url);
    }

    [Fact]
    public void Path_variables_are_found_and_replaced()
    {
        const string url = "{{base}}/users/:id/posts/:postId?x=:notAVar";
        Assert.Equal(["id", "postId"], UrlUtilities.ParsePathVariables(url));
        Assert.Equal("{{base}}/users/7/posts/:postId?x=:notAVar",
            UrlUtilities.ReplacePathVariables(url, new Dictionary<string, string> { ["id"] = "7" }));
    }

    [Fact]
    public void Port_is_not_a_path_variable()
    {
        Assert.Empty(UrlUtilities.ParsePathVariables("http://localhost:8080/a"));
    }

    [Theory]
    [InlineData("example.com/a", "http://example.com/a")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("  localhost:3000 ", "http://localhost:3000")]
    public void EnsureScheme(string input, string expected) => Assert.Equal(expected, UrlUtilities.EnsureScheme(input));

    [Fact]
    public void MergeQueryParams_replaces_enabled_rows_and_keeps_disabled_ones_in_place()
    {
        var existing = new List<KeyValueItem>
        {
            new("a", "1") { Description = "first" },
            new("off", "x", enabled: false),
            new("b", "2") { Description = "second" },
        };

        var merged = UrlUtilities.MergeQueryParams(existing, UrlUtilities.ParseQuery("a=9&b=8&c=7"));

        Assert.Equal(["a", "off", "b", "c"], merged.Select(r => r.Key));
        Assert.Equal(["9", "x", "8", "7"], merged.Select(r => r.Value));
        Assert.Equal(["first", null, "second", null], merged.Select(r => r.Description));
        Assert.False(merged[1].Enabled);
    }

    [Fact]
    public void MergeQueryParams_drops_enabled_rows_missing_from_the_url()
    {
        var existing = new List<KeyValueItem> { new("a", "1"), new("off", "x", enabled: false), new("b", "2") };

        var merged = UrlUtilities.MergeQueryParams(existing, UrlUtilities.ParseQuery("a=1"));

        Assert.Equal(["a", "off"], merged.Select(r => r.Key));
    }

    [Fact]
    public void MergeQueryParams_with_no_rows_returns_the_parsed_ones()
    {
        var merged = UrlUtilities.MergeQueryParams([], UrlUtilities.ParseQuery("q=1&r=2"));

        Assert.Equal(["q", "r"], merged.Select(r => r.Key));
    }

    [Fact]
    public void MergePathVariables_keeps_known_values_and_descriptions_and_drops_removed_names()
    {
        var existing = new List<KeyValueItem>
        {
            new("id", "7") { Description = "user id" },
            new("gone", "x"),
        };

        var merged = UrlUtilities.MergePathVariables("http://x/users/:id/posts/:postId", existing);

        Assert.Equal(["id", "postId"], merged.Select(v => v.Key));
        Assert.Equal(["7", ""], merged.Select(v => v.Value));
        Assert.Equal("user id", merged[0].Description);
        Assert.Null(merged[1].Description);
    }

    [Fact]
    public void MergePathVariables_with_repeated_existing_names_uses_the_first()
    {
        var merged = UrlUtilities.MergePathVariables("http://x/:id", [new("id", "1"), new("id", "2")]);

        Assert.Equal("1", Assert.Single(merged).Value);
    }
}
