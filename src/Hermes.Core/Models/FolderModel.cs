using Hermes.Core.Models.Auth;

namespace Hermes.Core.Models;

public sealed class FolderModel : CollectionNode
{
    public List<CollectionNode> Items { get; set; } = [];
    public AuthConfig Auth { get; set; } = AuthConfig.Inherit();

    /// <summary>
    /// Sent with every request in the folder and its subfolders, unless an inner folder or the request sets the same
    /// header. Overrides the collection's and outer folders' headers of the same name.
    /// </summary>
    public List<KeyValueItem> Headers { get; set; } = [];
}
