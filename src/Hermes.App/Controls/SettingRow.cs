using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Hermes.App.Controls;

/// <summary>
/// One setting: a title and a hint on the left and the control that changes it (the <see cref="ContentControl.Content"/>)
/// on the right, or below the text when <see cref="IsStacked"/> is set.
/// </summary>
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Hint));

    /// <summary>Optional content before the text, such as an icon.</summary>
    public static readonly StyledProperty<object?> LeadingProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Leading));

    /// <summary>Optional content under the hint, such as a validation message.</summary>
    public static readonly StyledProperty<object?> DetailsProperty =
        AvaloniaProperty.Register<SettingRow, object?>(nameof(Details));

    /// <summary>Put the control under the text instead of beside it, for wide controls such as a path box.</summary>
    public static readonly StyledProperty<bool> IsStackedProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsStacked));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public object? Leading
    {
        get => GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }

    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsStackedProperty)
            PseudoClasses.Set(":stacked", IsStacked);
    }
}
