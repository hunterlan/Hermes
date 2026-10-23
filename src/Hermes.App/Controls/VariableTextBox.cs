using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Hermes.Core.Variables;

namespace Hermes.App.Controls;

/// <summary>
/// Single-line text input that highlights <c>{{variables}}</c> (orange when resolved, red when not)
/// and shows the resolved value on hover, like Postman's URL bar. Typing <c>{{</c> opens a list of matching variables.
/// </summary>
public sealed class VariableTextBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<VariableScope?> VariablesProperty =
        AvaloniaProperty.Register<VariableTextBox, VariableScope?>(nameof(Variables));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(nameof(PlaceholderText));

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
    private readonly Popup _suggestionPopup;
    private readonly ListBox _suggestionList;
    private int _suggestionNameStart;
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
        _editor.TextArea.LostFocus += (_, _) =>
        {
            PseudoClasses.Set(":editing", false);
            CloseSuggestions();
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateSuggestions();

        _watermark = new TextBlock
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 0, 0),
            FontSize = 13,
        };
        _watermark.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextTertiaryBrush"));

        _suggestionList = new ListBox
        {
            Focusable = false,
            Background = Brushes.Transparent,
            MinWidth = 260,
            MaxWidth = 460,
            ItemTemplate = new FuncDataTemplate<VariableResolver.VariableSuggestion>((item, _) => BuildSuggestionRow(item)),
        };
        _suggestionList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(8, 5)), new Setter(MinHeightProperty, 0d) },
        });
        // Tunnel so the pick happens before the list could take focus away from the editor.
        _suggestionList.AddHandler(PointerPressedEvent, OnSuggestionPointerPressed, RoutingStrategies.Tunnel);
        var suggestionBorder = new Border
        {
            Child = _suggestionList,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
        };
        suggestionBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("BgPrimaryBrush"));
        suggestionBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BorderStrongBrush"));
        _suggestionPopup = new Popup
        {
            Child = suggestionBorder,
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = 4,
            IsLightDismissEnabled = false,
        };

        Content = new Panel { Children = { _watermark, _editor, _suggestionPopup } };
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
        ToolTip.SetShowDelay(this, 300);
        UpdatePlaceholderText();
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

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
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
            UpdatePlaceholderText();
        }
        else if (change.Property == VariablesProperty)
        {
            _colorizer.Scope = change.GetNewValue<VariableScope?>();
            _editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == PlaceholderTextProperty)
        {
            UpdatePlaceholderText();
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

    private void UpdatePlaceholderText()
    {
        _watermark.Text = PlaceholderText;
        _watermark.IsVisible = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(PlaceholderText);
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
        UpdateSuggestions();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_suggestionPopup.IsOpen && HandleSuggestionKey(e))
            return;

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

    public bool IsSuggestionListOpen => _suggestionPopup.IsOpen;

    public IReadOnlyList<string> SuggestionNames =>
        _suggestionList.ItemsSource is IEnumerable<VariableResolver.VariableSuggestion> items ? items.Select(i => i.Name).ToList() : [];

    private void UpdateSuggestions()
    {
        if (IsReadOnly || !_editor.TextArea.IsFocused ||
            !VariableResolver.TryFindPartialVariable(_editor.Text, _editor.CaretOffset, out var nameStart, out var typed))
        {
            CloseSuggestions();
            return;
        }

        var suggestions = VariableResolver.Suggest(Variables ?? VariableScope.Empty, typed);
        if (suggestions.Count == 0)
        {
            CloseSuggestions();
            return;
        }

        _suggestionNameStart = nameStart;
        _suggestionList.ItemsSource = suggestions;
        _suggestionList.SelectedIndex = 0;

        // Line the list up under the "{{" rather than the left edge of a long URL.
        var textView = _editor.TextArea.TextView;
        var location = _editor.Document.GetLocation(Math.Max(0, nameStart - 2));
        var x = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineBottom).X - textView.ScrollOffset.X;
        _suggestionPopup.HorizontalOffset = Math.Clamp(x, 0, Math.Max(0, Bounds.Width - 260));
        _suggestionPopup.IsOpen = true;
    }

    private void CloseSuggestions()
    {
        if (_suggestionPopup.IsOpen)
            _suggestionPopup.IsOpen = false;
    }

    private bool HandleSuggestionKey(KeyEventArgs e)
    {
        var count = _suggestionList.ItemCount;
        switch (e.Key)
        {
            case Key.Down:
                _suggestionList.SelectedIndex = (_suggestionList.SelectedIndex + 1) % count;
                break;
            case Key.Up:
                _suggestionList.SelectedIndex = (_suggestionList.SelectedIndex - 1 + count) % count;
                break;
            case Key.Enter or Key.Tab when e.KeyModifiers == KeyModifiers.None:
                if (_suggestionList.SelectedItem is not VariableResolver.VariableSuggestion pick)
                    return false;
                AcceptSuggestion(pick);
                break;
            case Key.Escape:
                CloseSuggestions();
                break;
            default:
                return false;
        }
        e.Handled = true;
        return true;
    }

    private void OnSuggestionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is VariableResolver.VariableSuggestion pick)
        {
            e.Handled = true;
            AcceptSuggestion(pick);
        }
    }

    /// <summary>Replaces the partial name under the caret with <paramref name="pick"/> and closes the braces.</summary>
    private void AcceptSuggestion(VariableResolver.VariableSuggestion pick)
    {
        var text = _editor.Text;
        // Also swallow the rest of a name being edited in place, e.g. the "rl" in "{{baseU|rl}}".
        var end = _editor.CaretOffset;
        while (end < text.Length && text[end] is not ('{' or '}') && !char.IsWhiteSpace(text[end]))
            end++;
        var closed = string.CompareOrdinal(text, end, "}}", 0, 2) == 0;
        var insert = pick.Name + "}}";
        _editor.Document.Replace(_suggestionNameStart, end - _suggestionNameStart + (closed ? 2 : 0), insert);
        _editor.CaretOffset = _suggestionNameStart + insert.Length;
        CloseSuggestions();
        _editor.TextArea.Focus();
    }

    private Control BuildSuggestionRow(VariableResolver.VariableSuggestion item)
    {
        var name = new TextBlock { Text = item.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        var value = new TextBlock
        {
            Text = item.Value is null ? "dynamic" : item.Value.Length == 0 ? "(empty)" : item.Value,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 0, 0),
        };
        value.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextTertiaryBrush"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(value, 1);
        row.Children.Add(name);
        row.Children.Add(value);
        return row;
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
                tip = scope.TryGetDisplay(match.Name, out var value)
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
