using Avalonia;
using Avalonia.Controls;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

/// <summary>Where a setting comes from: the levels of a collection → folders → request chain, linked, with the source highlighted.</summary>
public partial class InheritanceChainView : UserControl
{
    public static readonly StyledProperty<IEnumerable<InheritanceChainNode>?> NodesProperty =
        AvaloniaProperty.Register<InheritanceChainView, IEnumerable<InheritanceChainNode>?>(nameof(Nodes));

    public InheritanceChainView() => InitializeComponent();

    public IEnumerable<InheritanceChainNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }
}
