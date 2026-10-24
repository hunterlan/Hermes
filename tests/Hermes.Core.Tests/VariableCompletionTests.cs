using Hermes.Core.Variables;

namespace Hermes.Core.Tests;

public class VariableCompletionTests
{
    [Fact]
    public void Finds_the_partial_variable_before_the_caret()
    {
        Assert.True(VariableCompletion.TryFindPartialVariable("{{base}}/users/{{us", 19, out var start, out var typed));
        Assert.Equal((17, "us"), (start, typed));

        Assert.True(VariableCompletion.TryFindPartialVariable("x{{", 3, out _, out typed));
        Assert.Equal("", typed);

        Assert.False(VariableCompletion.TryFindPartialVariable("{{base}}/users", 14, out _, out _));
        Assert.False(VariableCompletion.TryFindPartialVariable("{{a b", 5, out _, out _));
    }

    [Fact]
    public void Suggests_at_most_five_names_with_prefix_matches_first()
    {
        var scope = new VariableScopeBuilder()
            .Set("apiUrl", "a").Set("baseUrl", "b").Set("authUrl", "c").Set("urlPrefix", "d")
            .Set("cdnUrl", "e").Set("fileUrl", "f").Set("token", "g")
            .Build();

        var names = VariableCompletion.Suggest(scope, "url").Select(s => s.Name).ToList();
        Assert.Equal(["urlPrefix", "apiUrl", "authUrl", "baseUrl", "cdnUrl"], names);
        Assert.Equal("token", Assert.Single(VariableCompletion.Suggest(scope, "TOK")).Name);
        Assert.Contains(VariableCompletion.Suggest(scope, "$gu"), s => s is { Name: "$guid", Value: null });
    }
}
