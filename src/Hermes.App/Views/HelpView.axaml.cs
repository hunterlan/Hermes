using Avalonia.Controls;
using Hermes.App.ViewModels;
using Markdown.Avalonia;

namespace Hermes.App.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
        // HyperlinkCommand is a plain property on the viewer engine, so it can't be bound in XAML.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HelpTabViewModel help && Viewer.Engine is IMarkdownEngine2 engine)
                engine.HyperlinkCommand = help.OpenLinkCommand;
        };
    }
}
