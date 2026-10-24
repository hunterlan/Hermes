using Hermes.Core.Models;

namespace Hermes.Core.Auth;

public static class AuthResolver
{
    /// <summary>
    /// Walks request → folders (innermost first) → collection until it finds an auth that isn't "inherit".
    /// </summary>
    /// <param name="folders">Ancestor folders, outermost first.</param>
    public static EffectiveAuth Resolve(AuthConfig own, IReadOnlyList<FolderModel> folders, CollectionModel? collection)
    {
        if (own.Type != AuthType.Inherit)
            return new EffectiveAuth(own, null);

        for (var i = folders.Count - 1; i >= 0; i--)
            if (folders[i].Auth.Type != AuthType.Inherit)
                return new EffectiveAuth(folders[i].Auth, folders[i].Name);

        if (collection is not null && collection.Auth.Type != AuthType.Inherit)
            return new EffectiveAuth(collection.Auth, collection.Name);

        return new EffectiveAuth(AuthConfig.NoAuth(), collection?.Name);
    }
}
