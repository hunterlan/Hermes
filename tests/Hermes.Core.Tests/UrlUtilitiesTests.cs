using Hermes.Core.Http;
using Hermes.Core.Models;

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
}
