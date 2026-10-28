using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hermes.App.Controls;

/// <summary>Coloured HTTP method label (GET green, POST yellow, ...), theme-aware.</summary>
public sealed class MethodBadge : TextBlock
{
    public static readonly StyledProperty<string?> MethodProperty =
        AvaloniaProperty.Register<MethodBadge, string?>(nameof(Method));

    /// <summary>Abbreviate long methods (DELETE → DEL) as Postman does in the sidebar.</summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<MethodBadge, bool>(nameof(Compact));

    private static readonly HashSet<string> KnownMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private IDisposable? _foregroundBinding;

    public MethodBadge()
    {
        FontWeight = FontWeight.SemiBold;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public string? Method
    {
        get => GetValue(MethodProperty);
        set => SetValue(MethodProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MethodProperty || change.Property == CompactProperty)
            Update();
    }

    private void Update()
    {
        var method = (Method ?? "").ToUpperInvariant();
        Text = Compact
            ? method switch
            {
                "DELETE" => "DEL",
                "OPTIONS" => "OPT",
                "PATCH" => "PTCH",
                _ => method,
            }
            : method;
        _foregroundBinding?.Dispose();
        var key = KnownMethods.Contains(method) ? $"Method{method}Brush" : "MethodOTHERBrush";
        _foregroundBinding = this.Bind(ForegroundProperty, this.GetResourceObservable(key));
    }
}
