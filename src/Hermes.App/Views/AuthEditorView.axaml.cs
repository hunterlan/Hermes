using Avalonia;
using Avalonia.Controls;
using Hermes.Core.Variables;

namespace Hermes.App.Views;

public partial class AuthEditorView : UserControl
{
    public static readonly StyledProperty<VariableScope?> VariablesProperty =
        AvaloniaProperty.Register<AuthEditorView, VariableScope?>(nameof(Variables));

    public AuthEditorView() => InitializeComponent();

    public VariableScope? Variables
    {
        get => GetValue(VariablesProperty);
        set => SetValue(VariablesProperty, value);
    }
}
