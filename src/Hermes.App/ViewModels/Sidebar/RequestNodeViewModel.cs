using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

public sealed class RequestNodeViewModel : TreeNodeViewModel
{
    public RequestNodeViewModel(HttpRequestModel model, TreeNodeViewModel parent) : base(parent)
    {
        Model = model;
        Name = model.Name;
    }

    public HttpRequestModel Model { get; }
    public override Guid Id => Model.Id;
    public override bool IsRequest => true;
    public string Method => Model.Method;
}
