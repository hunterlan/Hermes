using Avalonia;
using Avalonia.Controls;

namespace Hermes.App.Views;

/// <summary>
/// The Authorization tab body of a collection or folder: an explanation above the auth editor.
/// Its data context is the container's auth editor.
/// </summary>
public partial class ContainerAuthorizationSection : UserControl
{
    /// <summary>What the container is called in the explanation: "collection" or "folder".</summary>
    public static readonly StyledProperty<string?> ContainerNounProperty =
        AvaloniaProperty.Register<ContainerAuthorizationSection, string?>(nameof(ContainerNoun));

    public ContainerAuthorizationSection() => InitializeComponent();

    public string? ContainerNoun
    {
        get => GetValue(ContainerNounProperty);
        set => SetValue(ContainerNounProperty, value);
    }
}
