using Hermes.Core.Models;
using Hermes.Core.Models.Auth;

namespace Hermes.App.ViewModels;

public static class AuthInheritance
{
    /// <summary>The collection and folders above an item, outermost first.</summary>
    public static IReadOnlyList<(string Kind, string Name, AuthConfig Auth)> OuterLevels(Core.Requests.RequestContext? context)
    {
        if (context?.Collection is not { } collection)
            return [];
        return [("Collection", collection.Name, collection.Auth), .. context.Folders.Select(f => ("Folder", f.Name, f.Auth))];
    }

    public static string Label(AuthType type) =>
        AuthEditorViewModel.BaseTypes.FirstOrDefault(t => t.Value == type)?.Label ?? type.ToString();

    /// <summary>Levels from the outermost (collection) to <paramref name="self"/>; the nearest level that sets auth is the source.</summary>
    public static List<InheritanceChainNode> Chain(IReadOnlyList<(string Kind, string Name, AuthConfig Auth)> outerLevels, (string Kind, string Name, AuthConfig Auth) self)
    {
        var levels = outerLevels.Append(self).ToList();
        var source = levels.FindLastIndex(l => l.Auth.Type != AuthType.Inherit);
        return levels.Select((l, i) =>
        {
            var defines = l.Auth.Type != AuthType.Inherit;
            var status = i == source ? (i == levels.Count - 1 ? "Defined here" : "Source")
                : defines ? "Overridden"
                : "Inherits";
            IReadOnlyList<InheritanceChainDetail> details = defines ? [new InheritanceChainDetail(Label(l.Auth.Type))] : [];
            return new InheritanceChainNode($"{l.Kind} · {l.Name}", status, i == source, i == levels.Count - 1, details);
        }).ToList();
    }

    /// <summary>Items under a container that set their own auth, and how many requests end up using the container's auth.</summary>
    public static (List<AuthOverride> Overrides, int Inheriting, int Total) Usage(IEnumerable<CollectionNode> items)
    {
        var overrides = new List<AuthOverride>();
        var inheriting = 0;
        var total = 0;
        Walk(items, inherited: true);
        return (overrides, inheriting, total);

        void Walk(IEnumerable<CollectionNode> nodes, bool inherited)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case HttpRequestModel request:
                        total++;
                        if (request.Auth.Type != AuthType.Inherit)
                            overrides.Add(new AuthOverride(request.Method, request.Name, Label(request.Auth.Type)));
                        else if (inherited)
                            inheriting++;
                        break;
                    case FolderModel folder:
                        if (folder.Auth.Type != AuthType.Inherit)
                            overrides.Add(new AuthOverride(null, folder.Name, Label(folder.Auth.Type)));
                        Walk(folder.Items, inherited && folder.Auth.Type == AuthType.Inherit);
                        break;
                }
            }
        }
    }
}
