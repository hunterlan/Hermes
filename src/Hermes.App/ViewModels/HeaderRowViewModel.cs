using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermes.App.ViewModels;

/// <summary>
/// A header that can't be edited in its list. A plain row has a key, a value and a short tag saying where it comes
/// from. A row inherited from a folder or collection can also have a chip naming (and opening) its source, a checkbox
/// to skip it, and an overridden state (crossed out, with a tag saying so).
/// </summary>
public sealed partial class HeaderRowViewModel(string key, string value, string tagText) : ObservableObject
{
    private const string SkippedTagText = "skipped here";

    private bool _isSent = true;
    private string _tagText = tagText;
    private bool _isTagWarning;

    public string Key { get; } = key;
    public string Value { get; } = value;

    /// <summary>Where the header comes from, or a warning about it. Empty for none.</summary>
    public string TagText
    {
        get => _tagText;
        init => _tagText = value;
    }

    /// <summary>The tag is a warning: shown with an icon and the warning colours.</summary>
    public bool IsTagWarning
    {
        get => _isTagWarning;
        init => _isTagWarning = value;
    }

    /// <summary>Name of the folder or collection the header is set on; the chip is shown when this is set.</summary>
    public string? SourceName { get; init; }

    /// <summary>Whether the source is a collection (rather than a folder), for the chip's icon.</summary>
    public bool IsSourceCollection { get; init; }

    /// <summary>Opens the source's tab. The chip does nothing without it.</summary>
    public Action? OpenSource { get; init; }

    /// <summary>The row has a checkbox to skip it (unticked = skipped) and calls <see cref="SentChanged"/>.</summary>
    public bool CanSkip { get; init; }

    /// <summary>Called with the new <see cref="IsSent"/> when the user ticks or unticks the row.</summary>
    public Action<HeaderRowViewModel, bool>? SentChanged { get; init; }

    /// <summary>The header is crossed out because a nearer level sets the same name.</summary>
    public bool IsOverridden { get; init; }

    /// <summary>Tag of an overridden row: "overridden above" for the request, "overridden here" on a folder's tab.</summary>
    public string OverriddenTagText { get; init; } = "overridden above";

    public bool HasSource => SourceName is not null;

    /// <summary>An overridden row's checkbox is disabled: the nearer header decides, so skipping it changes nothing.</summary>
    public bool CanToggleSent => !IsOverridden;

    /// <summary>Sets <see cref="IsSent"/> when the row is built, without calling <see cref="SentChanged"/>.</summary>
    public bool InitiallySent
    {
        init => _isSent = value;
    }

    /// <summary>The row's checkbox: ticked when the header is sent, unticked when skipped.</summary>
    public bool IsSent
    {
        get => _isSent;
        set
        {
            if (!SetProperty(ref _isSent, value))
                return;
            OnPropertyChanged(nameof(IsSkipped));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(ShowWarning));
            SentChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Updates the row in place, without calling <see cref="SentChanged"/>. Used after another row is toggled, so
    /// rows (and the keyboard focus on the one just toggled) are kept instead of rebuilt.
    /// </summary>
    public void Update(bool isSent, string tagText, bool isTagWarning)
    {
        _isSent = isSent;
        _tagText = tagText;
        _isTagWarning = isTagWarning;
        OnPropertyChanged(nameof(IsSent));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(IsTagWarning));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(ShowWarning));
    }

    public bool IsSkipped => CanSkip && !_isSent && !IsOverridden;

    /// <summary>The tag to show: the overridden or skipped state, else <see cref="TagText"/>. Never colour alone.</summary>
    public string StatusText => IsOverridden ? OverriddenTagText : IsSkipped ? SkippedTagText : TagText;

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>The warning icon is only for the row's own tag, not for the overridden or skipped state.</summary>
    public bool ShowWarning => IsTagWarning && !IsOverridden && !IsSkipped;

    public string SourceToolTip => HasSource ? $"Open {SourceName}" : "";

    public string CheckBoxName => $"Send {Key} header";

    [RelayCommand]
    private void OpenSourceTab() => OpenSource?.Invoke();
}
