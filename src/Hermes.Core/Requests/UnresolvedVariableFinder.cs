using Hermes.Core.Models;
using Hermes.Core.Variables;

namespace Hermes.Core.Requests;

/// <summary>Finds the variables a request uses that have nothing to resolve to in its <see cref="RequestContext"/>.</summary>
public static class UnresolvedVariableFinder
{
    /// <summary>Variables used by the request that resolve to nothing; they would be sent as literal {{name}} text.</summary>
    public static IReadOnlyList<string> Find(HttpRequestModel model, RequestContext context)
    {
        var scope = context.BuildScope();
        IEnumerable<string?> texts =
        [
            model.Url,
            .. model.PathVariables.Select(p => p.Value),
            .. context.ResolveHeaders(model).HeadersToSend.SelectMany(h => new[] { h.Key, h.Value }),
            .. model.Body.Mode switch
            {
                BodyMode.Raw => [model.Body.Raw],
                BodyMode.FormData => model.Body.FormData.Where(f => f.Enabled).SelectMany(f => new[] { f.Key, f.Value }),
                BodyMode.UrlEncoded => model.Body.UrlEncoded.Where(f => f.Enabled).SelectMany(f => new[] { f.Key, f.Value }),
                _ => [],
            },
        ];
        return texts.SelectMany(VariableResolver.FindVariables)
            .Select(m => m.Name)
            .Where(name => !VariableResolver.CanResolve(name, scope) || scope.TryGet(name, out var value) && value.Length == 0)
            .Distinct()
            .ToList();
    }
}
