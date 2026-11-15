using Hermes.Core.Models;
using Hermes.Core.Requests;

namespace Hermes.Core.Tests;

public class UnresolvedVariableFinderTests
{
    private static RequestContext ContextWith(params Variable[] variables) =>
        new() { Environment = new EnvironmentModel { Variables = [.. variables] } };

    [Fact]
    public void Finds_nothing_when_every_variable_has_a_value()
    {
        var request = new HttpRequestModel { Url = "{{base}}/items" };

        Assert.Empty(UnresolvedVariableFinder.Find(request, ContextWith(new Variable("base", "http://x"))));
    }

    [Fact]
    public void Finds_unknown_variables_once_each_in_the_order_of_use()
    {
        var request = new HttpRequestModel { Url = "{{base}}/{{id}}/{{base}}" };

        Assert.Equal(["base", "id"], UnresolvedVariableFinder.Find(request, RequestContext.Empty));
    }

    [Fact]
    public void A_variable_with_an_empty_value_counts_as_unresolved()
    {
        var request = new HttpRequestModel { Url = "http://x/{{empty}}" };

        Assert.Equal(["empty"], UnresolvedVariableFinder.Find(request, ContextWith(new Variable("empty", ""))));
    }

    [Fact]
    public void Dynamic_variables_are_not_unresolved()
    {
        var request = new HttpRequestModel { Url = "http://x/{{$guid}}" };

        Assert.Empty(UnresolvedVariableFinder.Find(request, RequestContext.Empty));
    }

    [Fact]
    public void Looks_in_path_variables_headers_and_the_body_of_the_active_mode()
    {
        var request = new HttpRequestModel
        {
            Url = "http://x/:id",
            PathVariables = [new("id", "{{inPath}}")],
            Headers = [new("X-{{inHeaderKey}}", "{{inHeaderValue}}"), new("X-Off", "{{inDisabledHeader}}", enabled: false)],
            Body = new RequestBody
            {
                Mode = BodyMode.UrlEncoded,
                Raw = "{{inInactiveRaw}}",
                UrlEncoded = [new("a", "{{inForm}}"), new("b", "{{inDisabledForm}}", enabled: false)],
            },
        };

        Assert.Equal(["inPath", "inHeaderKey", "inHeaderValue", "inForm"], UnresolvedVariableFinder.Find(request, RequestContext.Empty));
    }

    [Fact]
    public void Looks_in_the_raw_body_when_the_mode_is_raw()
    {
        var request = new HttpRequestModel { Url = "http://x", Body = new RequestBody { Mode = BodyMode.Raw, Raw = "{\"a\":\"{{token}}\"}" } };

        Assert.Equal(["token"], UnresolvedVariableFinder.Find(request, RequestContext.Empty));
    }
}
