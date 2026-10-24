using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

public sealed class FolderNodeViewModel : TreeNodeViewModel
{
    public FolderNodeViewModel(FolderModel model, TreeNodeViewModel parent) : base(parent)
    {
        Model = model;
        Name = model.Name;
        foreach (var item in model.Items)
            Children.Add(CollectionNodeViewModel.Create(item, this));
    }

    public FolderModel Model { get; }
    public override Guid Id => Model.Id;
    public override bool IsFolder => true;
}
