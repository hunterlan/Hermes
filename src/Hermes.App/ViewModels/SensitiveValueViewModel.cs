using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Headers;

namespace Hermes.App.ViewModels;

/// <summary>
/// A header value that may be sensitive: masked with dots until revealed, copied whole even while masked, and
/// expandable into a box that shows all of it. Every kind of header row shares this, so they all behave alike.
/// </summary>
public sealed partial class SensitiveValueViewModel(string value, bool isSensitive, Func<string, Task>? copyText)
    : ObservableObject
{
    /// <summary>A longer value gets an expand button even when it isn't sensitive, since the row shows one line.</summary>
    public const int ExpandableLength = 60;

    private static readonly TimeSpan DefaultCopiedDuration = TimeSpan.FromSeconds(1.5);

    private string _value = value;
    private bool _isSensitive = isSensitive;

    [ObservableProperty] public partial bool IsRevealed { get; set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    /// <summary>The value was just copied; shown briefly as a confirmation.</summary>
    [ObservableProperty] public partial bool IsCopied { get; set; }

    /// <summary>Called with the new <see cref="IsRevealed"/> when it changes, to keep it across rebuilt rows.</summary>
    public Action<bool>? RevealChanged { get; init; }

    /// <summary>How long <see cref="IsCopied"/> stays set.</summary>
    public TimeSpan CopiedDuration { get; init; } = DefaultCopiedDuration;

    /// <summary>The real value, whether or not it is masked.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value))
                return;
            NotifyDerivedProperties();
        }
    }

    public bool IsSensitive
    {
        get => _isSensitive;
        set
        {
            if (!SetProperty(ref _isSensitive, value))
                return;
            NotifyDerivedProperties();
        }
    }

    /// <summary>The dots are shown instead of the value. An empty value has nothing to hide.</summary>
    public bool ShowMask => IsSensitive && !IsRevealed && Value.Length > 0;

    /// <summary>What a row shows: the dots, or the value.</summary>
    public string DisplayValue => ShowMask ? SensitiveHeaders.MaskText : Value;

    /// <summary>"1,734 characters" (or "1 character").</summary>
    public string CharacterCountText => Value.Length == 1
        ? "1 character"
        : $"{Value.Length.ToString("N0", CultureInfo.InvariantCulture)} characters";

    /// <summary>The row has an expand button: it is sensitive, or too long to read on one line.</summary>
    public bool CanExpand => IsSensitive || Value.Length > ExpandableLength;

    public string RevealToolTip => IsRevealed ? "Hide value" : "Reveal value";

    /// <summary>Masks the value again and closes its expanded box.</summary>
    public void Hide()
    {
        IsRevealed = false;
        IsExpanded = false;
    }

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMask));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealToolTip));
        RevealChanged?.Invoke(value);
    }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void HideValue() => Hide();

    /// <summary>Copies the real value, also while it is masked, and shows the confirmation for a moment.</summary>
    [RelayCommand]
    private async Task Copy()
    {
        if (copyText is null)
            return;
        await copyText(Value);
        IsCopied = true;
        await Task.Delay(CopiedDuration);
        IsCopied = false;
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(ShowMask));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(CharacterCountText));
        OnPropertyChanged(nameof(CanExpand));
    }
}
