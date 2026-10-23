using Hermes.Core.Models;
using Hermes.Core.Variables;

namespace Hermes.Core.Tests;

public class VariableResolverTests
{
    [Fact]
    public void Resolves_with_environment_overriding_collection_and_globals()
    {
        var scope = new VariableScope()
            .AddLayer([new Variable("host", "global"), new Variable("g", "1")])
            .AddLayer([new Variable("host", "collection")])
            .AddLayer([new Variable("host", "env")]);

        Assert.Equal("env/1", VariableResolver.Resolve("{{host}}/{{g}}", scope));
    }

    [Fact]
    public void Leaves_unknown_variables_untouched()
    {
        Assert.Equal("a {{missing}} b", VariableResolver.Resolve("a {{missing}} b", VariableScope.Empty));
    }

    [Fact]
    public void Resolves_nested_variables()
    {
        var scope = new VariableScope().Set("base", "{{scheme}}://x").Set("scheme", "https");
        Assert.Equal("https://x/a", VariableResolver.Resolve("{{base}}/a", scope));
    }

    [Fact]
    public void Self_reference_does_not_loop_forever()
    {
        var scope = new VariableScope().Set("a", "{{a}}");
        Assert.Equal("{{a}}", VariableResolver.Resolve("{{a}}", scope));
    }

    [Fact]
    public void Disabled_variables_and_current_values()
    {
        var scope = new VariableScope().AddLayer(
        [
            new Variable("a", "initial") { CurrentValue = "current" },
            new Variable("b", "x") { Enabled = false },
        ]);
        Assert.Equal("current {{b}}", VariableResolver.Resolve("{{a}} {{b}}", scope));
    }

    [Fact]
    public void Dynamic_variables_resolve()
    {
        var guid = VariableResolver.Resolve("{{$guid}}", VariableScope.Empty);
        Assert.True(Guid.TryParse(guid, out _));
        Assert.Matches(@"^\d+$", VariableResolver.Resolve("{{$timestamp}}", VariableScope.Empty));
    }

    [Fact]
    public void FindVariables_reports_positions()
    {
        var matches = VariableResolver.FindVariables("x{{a}}y{{ b }}").ToList();
        Assert.Equal(2, matches.Count);
        Assert.Equal((1, 5, "a"), (matches[0].Start, matches[0].Length, matches[0].Name));
        Assert.Equal("b", matches[1].Name);
    }

    [Fact]
    public void Finds_the_partial_variable_before_the_caret()
    {
        Assert.True(VariableResolver.TryFindPartialVariable("{{base}}/users/{{us", 19, out var start, out var typed));
        Assert.Equal((17, "us"), (start, typed));

        Assert.True(VariableResolver.TryFindPartialVariable("x{{", 3, out _, out typed));
        Assert.Equal("", typed);

        Assert.False(VariableResolver.TryFindPartialVariable("{{base}}/users", 14, out _, out _));
        Assert.False(VariableResolver.TryFindPartialVariable("{{a b", 5, out _, out _));
    }

    [Fact]
    public void Suggests_at_most_five_names_with_prefix_matches_first()
    {
        var scope = new VariableScope()
            .Set("apiUrl", "a").Set("baseUrl", "b").Set("authUrl", "c").Set("urlPrefix", "d")
            .Set("cdnUrl", "e").Set("fileUrl", "f").Set("token", "g");

        var names = VariableResolver.Suggest(scope, "url").Select(s => s.Name).ToList();
        Assert.Equal(["urlPrefix", "apiUrl", "authUrl", "baseUrl", "cdnUrl"], names);
        Assert.Equal("token", Assert.Single(VariableResolver.Suggest(scope, "TOK")).Name);
        Assert.Contains(VariableResolver.Suggest(scope, "$gu"), s => s is { Name: "$guid", Value: null });
    }
}
