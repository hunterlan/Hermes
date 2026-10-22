using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Hermes.Core.Variables;

namespace Hermes.App.Controls;

/// <summary>
/// Single-line text input that highlights <c>{{variables}}</c> (orange when resolved, red when not)
/// and shows the resolved value on hover, like Postman's URL bar.
/// </summary>
public sealed class VariableTextBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<VariableScope?> VariablesProperty =
        AvaloniaProperty.Register<VariableTextBox, VariableScope?>(nameof(Variables));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.Register<VariableTextBox, ICommand?>(nameof(SubmitCommand));

    /// <summary>
    /// When true, pasted multi-line text reaches the binding as-is (the URL bar uses this to detect cURL);
    /// otherwise newlines are stripped.
    /// </summary>
    public static readonly StyledProperty<bool> AllowMultilineInputProperty =
        AvaloniaProperty.Register<VariableTextBox, bool>(nameof(AllowMultilineInput));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<VariableTextBox, bool>(nameof(IsReadOnly));

    private readonly TextEditor _editor;
    private readonly TextBlock _watermark;
    private readonly VariableColorizer _colorizer = new();
    private bool _updatingFromEditor;

    public VariableTextBox()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = false,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = false;
        _editor.Options.AllowScrollBelowDocument = false;
        _editor.Options.ShowEndOfLine = false;
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.TextChanged += OnEditorTextChanged;
        _editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.TextArea.TextView.PointerMoved += OnPointerMoved;
        _editor.TextArea.TextView.PointerExited += (_, _) => ToolTip.SetIsOpen(this, false);
        _editor.TextArea.GotFocus += (_, _) => PseudoClasses.Set(":editing", true);
        _editor.TextArea.LostFocus += (_, _) => PseudoClasses.Set(":editing", false);

        _watermark = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 0, 0),
            FontSize = 13,
        };
        _watermark.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextTertiaryBrush"));

        Content = new Panel { Children = { _watermark, _editor } };
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 300);
        UpdateWatermark();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public VariableScope? Variables
    {
        get => GetValue(VariablesProperty);
        set => SetValue(VariablesProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public ICommand? SubmitCommand
    {
        get => GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    public bool AllowMultilineInput
    {
        get => GetValue(AllowMultilineInputProperty);
        set => SetValue(AllowMultilineInputProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public void FocusEditor() => _editor.TextArea.Focus();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
        UpdateColors();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateColors();

    private void UpdateColors()
    {
        IBrush Brush(string key) =>
            this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush b ? b : Brushes.Gray;
        _colorizer.Resolved = Brush("VarResolvedBrush");
        _colorizer.ResolvedBackground = Brush("VarResolvedBgBrush");
        _colorizer.Unresolved = Brush("VarUnresolvedBrush");
        _colorizer.UnresolvedBackground = Brush("VarUnresolvedBgBrush");
        _editor.Foreground = Brush("TextPrimaryBrush");
        _editor.TextArea.TextView.Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (!_updatingFromEditor)
            {
                var text = change.GetNewValue<string?>() ?? "";
                if (_editor.Text != text)
                {
                    var caret = _editor.CaretOffset;
                    _editor.Document.Text = text;
                    _editor.CaretOffset = Math.Min(caret, text.Length);
                }
            }
            UpdateWatermark();
        }
        else if (change.Property == VariablesProperty)
        {
            _colorizer.Scope = change.GetNewValue<VariableScope?>();
            _editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == WatermarkProperty)
        {
            UpdateWatermark();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == FontFamilyProperty)
        {
            _editor.FontFamily = change.GetNewValue<FontFamily>();
        }
    }

    private void UpdateWatermark()
    {
        _watermark.Text = Watermark;
        _watermark.IsVisible = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Watermark);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        var text = _editor.Text;
        if (!AllowMultilineInput && (text.Contains('\n') || text.Contains('\r')))
        {
            // Strip newlines from pasted text; deferred because the document is mid-update.
            Dispatcher.UIThread.Post(() =>
            {
                var caret = _editor.CaretOffset;
                _editor.Document.Text = _editor.Text.Replace("\r", "").Replace("\n", "");
                _editor.CaretOffset = Math.Min(caret, _editor.Document.TextLength);
            });
            return;
        }

        _updatingFromEditor = true;
        try
        {
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _updatingFromEditor = false;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                if (SubmitCommand?.CanExecute(null) == true)
                    SubmitCommand.Execute(null);
                break;
            case Key.Tab:
                // Let Tab move focus instead of inserting a tab character.
                e.Handled = true;
                TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? NavigationDirection.Previous
                    : NavigationDirection.Next);
                break;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        var position = textView.GetPositionFloor(e.GetPosition(textView) + textView.ScrollOffset);
        string? tip = null;
        if (position is { } pos)
        {
            var offset = _editor.Document.GetOffset(pos.Location);
            foreach (var match in VariableResolver.FindVariables(_editor.Text))
            {
                if (offset < match.Start || offset > match.Start + match.Length)
                    continue;
                var scope = Variables ?? VariableScope.Empty;
                tip = scope.TryGet(match.Name, out var value)
                    ? $"{match.Name}\n{(value.Length == 0 ? "(empty)" : value)}"
                    : VariableResolver.IsDynamic(match.Name) && VariableResolver.CanResolve(match.Name, scope)
                        ? $"{match.Name}\nDynamic variable, generated when the request is sent"
                        : $"{match.Name}\nUnresolved variable. Define it in an environment, globals or collection variables.";
                break;
            }
        }

        if (tip is null)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }
        else if (!Equals(ToolTip.GetTip(this), tip))
        {
            ToolTip.SetTip(this, tip);
            ToolTip.SetIsOpen(this, true);
        }
    }

    private sealed class VariableColorizer : DocumentColorizingTransformer
    {
        public VariableScope? Scope { get; set; }
        public IBrush Resolved { get; set; } = Brushes.Orange;
        public IBrush ResolvedBackground { get; set; } = Brushes.Transparent;
        public IBrush Unresolved { get; set; } = Brushes.Red;
        public IBrush UnresolvedBackground { get; set; } = Brushes.Transparent;

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            foreach (var match in VariableResolver.FindVariables(text))
            {
                var ok = VariableResolver.CanResolve(match.Name, Scope ?? VariableScope.Empty);
                ChangeLinePart(line.Offset + match.Start, line.Offset + match.Start + match.Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(ok ? Resolved : Unresolved);
                    element.BackgroundBrush = ok ? ResolvedBackground : UnresolvedBackground;
                });
            }
        }
    }
}
