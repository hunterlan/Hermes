using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace Hermes.App.Controls;

/// <summary>
/// AvaloniaEdit wrapper with a bindable <see cref="Text"/>, TextMate highlighting by <see cref="Language"/>,
/// brace / XML folding and the built-in search panel (Ctrl+F).
/// </summary>
public sealed class CodeEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<CodeEditor, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<CodeEditor, string>(nameof(Language), "text");

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(WordWrap));

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<CodeEditor, bool>(nameof(ShowLineNumbers), true);

    private static readonly RegistryOptions DarkRegistry = new(ThemeName.DarkPlus);
    private static readonly RegistryOptions LightRegistry = new(ThemeName.LightPlus);

    private readonly TextEditor _editor;
    private readonly DispatcherTimer _foldingTimer;
    private TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private FoldingManager? _foldings;
    private bool _updatingFromEditor;

    public CodeEditor()
    {
        _editor = new TextEditor
        {
            FontFamily = (FontFamily?)Application.Current?.FindResource("MonoFont") ?? FontFamily.Default,
            FontSize = 13,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 4, 0, 4),
        };
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.HighlightCurrentLine = false;
        _editor.TextChanged += OnEditorTextChanged;
        SearchPanel.Install(_editor);
        Content = _editor;

        _foldingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _foldingTimer.Tick += (_, _) =>
        {
            _foldingTimer.Stop();
            UpdateFoldings();
        };
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnThemeChanged;
        ApplyTheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
        _textMate?.Dispose();
        _textMate = null;
        _registry = null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        var registry = ActualThemeVariant == ThemeVariant.Light ? LightRegistry : DarkRegistry;
        if (_textMate is null)
        {
            _registry = registry;
            _textMate = _editor.InstallTextMate(registry);
            ApplyLanguage();
        }
        else if (_registry != registry)
        {
            _registry = registry;
            _textMate.SetTheme(registry.LoadTheme(registry == LightRegistry ? ThemeName.LightPlus : ThemeName.DarkPlus));
        }
        _editor.Background = Brushes.Transparent;
        if (this.TryFindResource("TextPrimaryBrush", ActualThemeVariant, out var fg) && fg is IBrush brush)
            _editor.Foreground = brush;
        if (this.TryFindResource("TextTertiaryBrush", ActualThemeVariant, out var ln) && ln is IBrush lineNumbers)
            _editor.LineNumbersForeground = lineNumbers;
    }

    private void ApplyLanguage()
    {
        if (_textMate is null || _registry is null)
            return;
        var languageId = Language switch
        {
            "json" => "json",
            "xml" => "xml",
            "html" => "html",
            "javascript" => "javascript",
            "css" => "css",
            _ => null,
        };
        var scope = languageId is null ? null : _registry.GetScopeByLanguageId(languageId);
        _textMate.SetGrammar(scope);
        ResetFoldings();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (_updatingFromEditor)
                return;
            var text = change.GetNewValue<string?>() ?? "";
            if (_editor.Text != text)
            {
                _editor.Document.Text = text;
                _editor.Document.UndoStack.ClearAll();
                _editor.ScrollToHome();
            }
            ScheduleFoldings();
        }
        else if (change.Property == LanguageProperty)
        {
            ApplyLanguage();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _editor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == WordWrapProperty)
        {
            _editor.WordWrap = change.GetNewValue<bool>();
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            _editor.ShowLineNumbers = change.GetNewValue<bool>();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        _updatingFromEditor = true;
        try
        {
            SetCurrentValue(TextProperty, _editor.Text);
        }
        finally
        {
            _updatingFromEditor = false;
        }
        ScheduleFoldings();
    }

    // ---- folding ----

    private bool FoldingEnabled => Language is "json" or "javascript" or "xml" or "html" && _editor.Document.TextLength < 2_000_000;

    private void ResetFoldings()
    {
        if (_foldings is not null)
        {
            FoldingManager.Uninstall(_foldings);
            _foldings = null;
        }
        ScheduleFoldings();
    }

    private void ScheduleFoldings()
    {
        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    private void UpdateFoldings()
    {
        if (!FoldingEnabled)
        {
            if (_foldings is not null)
            {
                FoldingManager.Uninstall(_foldings);
                _foldings = null;
            }
            return;
        }

        _foldings ??= FoldingManager.Install(_editor.TextArea);
        if (Language is "xml" or "html")
            new XmlFoldingStrategy().UpdateFoldings(_foldings, _editor.Document);
        else
            BraceFoldingStrategy.UpdateFoldings(_foldings, _editor.Document);
    }
}
