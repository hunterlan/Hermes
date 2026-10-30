using Avalonia;
using Avalonia.Controls;
using Hermes.Core.Variables;

namespace Hermes.App.Controls;

/// <summary>
/// Carries the <c>{{variables}}</c> in scope down to every <see cref="VariableTextBox"/> below the element it is set on,
/// so a tab sets it once instead of each editor being handed the scope.
/// </summary>
public sealed class VariableScopeProperties : AvaloniaObject
{
    public static readonly AttachedProperty<VariableScope?> VariablesProperty =
        AvaloniaProperty.RegisterAttached<VariableScopeProperties, Control, VariableScope?>("Variables", inherits: true);

    // Not a static class because a static class can't be a generic type argument; nothing should create one.
    private VariableScopeProperties()
    {
    }

    public static VariableScope? GetVariables(Control element) => element.GetValue(VariablesProperty);

    public static void SetVariables(Control element, VariableScope? value) => element.SetValue(VariablesProperty, value);
}
