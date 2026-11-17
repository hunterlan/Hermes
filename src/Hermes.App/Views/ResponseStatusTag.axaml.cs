using Avalonia.Controls;

namespace Hermes.App.Views;

/// <summary>The response's status tag, or "No response" after a failure; nothing before the first send.</summary>
public partial class ResponseStatusTag : UserControl
{
    public ResponseStatusTag() => InitializeComponent();
}
