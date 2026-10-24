using Hermes.Core.Auth;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Variables;

namespace Hermes.Core.Http;

/// <summary>Everything outside the request itself that affects how it is sent.</summary>
public sealed class RequestContext
{
    public CollectionModel? Collection { get; init; }

    /// <summary>Ancestor folders of the request, outermost first.</summary>
    public IReadOnlyList<FolderModel> Folders { get; init; } = [];

    public EnvironmentModel? Environment { get; init; }
    public EnvironmentModel? Globals { get; init; }
    public AppSettings Settings { get; init; } = new();

    public static RequestContext Empty { get; } = new();

    /// <summary>Precedence (lowest → highest): globals, collection, environment.</summary>
    public VariableScope BuildScope() =>
        new VariableScopeBuilder()
            .AddLayer(Globals?.Variables)
            .AddLayer(Collection?.Variables)
            .AddLayer(Environment?.Variables)
            .Build();

    public EffectiveAuth ResolveAuth(AuthConfig own) => AuthResolver.Resolve(own, Folders, Collection);
}
