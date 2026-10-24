using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

public sealed class CollectionNodeViewModel : TreeNodeViewModel
{
    public CollectionNodeViewModel(CollectionModel model) : base(null)
    {
        Model = model;
        Name = model.Name;
        foreach (var item in model.Items)
            Children.Add(Create(item, this));
    }

    public CollectionModel Model { get; }
    public override Guid Id => Model.Id;
    public override bool IsCollection => true;

    internal static TreeNodeViewModel Create(CollectionNode node, TreeNodeViewModel parent) => node switch
    {
        FolderModel folder => new FolderNodeViewModel(folder, parent),
        HttpRequestModel request => new RequestNodeViewModel(request, parent),
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };
}
