using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Hermes.App.Controls;

/// <summary>
/// The button row at the bottom of a dialog. The <see cref="ContentControl.Content"/> (usually Cancel and an accent
/// button) is right-aligned, <see cref="Leading"/> sits at the left, and <see cref="CloseButtonText"/> adds a
/// button that simply closes the dialog, at the right before the content or, with
/// <see cref="CloseButtonOnLeading"/>, at the left edge ahead of <see cref="Leading"/>.
/// </summary>
public sealed class DialogFooter : ContentControl
{
    public static readonly StyledProperty<object?> LeadingProperty =
        AvaloniaProperty.Register<DialogFooter, object?>(nameof(Leading));

    public static readonly StyledProperty<string?> CloseButtonTextProperty =
        AvaloniaProperty.Register<DialogFooter, string?>(nameof(CloseButtonText));

    public static readonly StyledProperty<bool> CloseButtonOnLeadingProperty =
        AvaloniaProperty.Register<DialogFooter, bool>(nameof(CloseButtonOnLeading));

    // Which of the template's two close buttons shows; worked out from the two properties above for the template.
    public static readonly StyledProperty<bool> ShowsTrailingCloseButtonProperty =
        AvaloniaProperty.Register<DialogFooter, bool>(nameof(ShowsTrailingCloseButton));

    public static readonly StyledProperty<bool> ShowsLeadingCloseButtonProperty =
        AvaloniaProperty.Register<DialogFooter, bool>(nameof(ShowsLeadingCloseButton));

    private Button? _trailingCloseButton;
    private Button? _leadingCloseButton;

    /// <summary>Content at the left edge, such as a link-style action.</summary>
    public object? Leading
    {
        get => GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }

    /// <summary>When set, shows a button with this text that closes the window and answers the Escape key.</summary>
    public string? CloseButtonText
    {
        get => GetValue(CloseButtonTextProperty);
        set => SetValue(CloseButtonTextProperty, value);
    }

    /// <summary>Puts the close button at the left edge, apart from the buttons at the right that make a choice.</summary>
    public bool CloseButtonOnLeading
    {
        get => GetValue(CloseButtonOnLeadingProperty);
        set => SetValue(CloseButtonOnLeadingProperty, value);
    }

    public bool ShowsTrailingCloseButton => GetValue(ShowsTrailingCloseButtonProperty);

    public bool ShowsLeadingCloseButton => GetValue(ShowsLeadingCloseButtonProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != CloseButtonTextProperty && change.Property != CloseButtonOnLeadingProperty)
            return;
        var hasText = !string.IsNullOrEmpty(CloseButtonText);
        SetValue(ShowsTrailingCloseButtonProperty, hasText && !CloseButtonOnLeading);
        SetValue(ShowsLeadingCloseButtonProperty, hasText && CloseButtonOnLeading);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Detach(ref _trailingCloseButton);
        Detach(ref _leadingCloseButton);
        _trailingCloseButton = Attach(e, "PART_CloseButton");
        _leadingCloseButton = Attach(e, "PART_LeadingCloseButton");
    }

    private void Detach(ref Button? button)
    {
        if (button is not null)
            button.Click -= OnCloseClick;
        button = null;
    }

    private Button? Attach(TemplateAppliedEventArgs e, string name)
    {
        var button = e.NameScope.Find<Button>(name);
        if (button is not null)
            button.Click += OnCloseClick;
        return button;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
