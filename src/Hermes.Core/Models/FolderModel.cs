using Hermes.Core.Models.Auth;

namespace Hermes.Core.Models;

public sealed class FolderModel : CollectionNode
{
    public List<CollectionNode> Items { get; set; } = [];
    public AuthConfig Auth { get; set; } = AuthConfig.Inherit();
}
