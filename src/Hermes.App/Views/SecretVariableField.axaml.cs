using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

/// <summary>
/// A bordered <see cref="Controls.VariableTextBox"/> for a credential (a token, an API key). While
/// <see cref="Secret"/> says so it shows dots, until the field has focus (to edit it) or the eye reveals it. Variables
/// keep their highlighting and suggestions, unlike in a password box.
/// </summary>
public partial class SecretVariableField : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SecretVariableField, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<SecretVariableField, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<SensitiveValueViewModel?> SecretProperty =
        AvaloniaProperty.Register<SecretVariableField, SensitiveValueViewModel?>(nameof(Secret));

    public SecretVariableField() => InitializeComponent();

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Whether the value is masked and revealed; kept by the view model so leaving the tab masks it again.</summary>
    public SensitiveValueViewModel? Secret
    {
        get => GetValue(SecretProperty);
        set => SetValue(SecretProperty, value);
    }
}
