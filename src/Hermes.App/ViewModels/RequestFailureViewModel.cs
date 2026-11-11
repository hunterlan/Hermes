using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>
/// One request failure as the response pane and the Auth panel show it: the display values, whether the collapsed
/// Details are open, and copying the details. Panels that act on the failure's next steps read <see cref="Model"/>.
/// </summary>
public sealed partial class RequestFailureViewModel(RequestFailure failure, Func<string, Task> copyText) : ObservableObject
{
    /// <summary>The failure as Core described it.</summary>
    public RequestFailure Model { get; } = failure;

    /// <summary>Whether the Details are open; they start closed for each new failure.</summary>
    [ObservableProperty] public partial bool IsDetailsExpanded { get; set; }

    public string Headline => Model.Headline;
    public string Facts => Model.Facts;
    public bool NothingWasSent => Model.NothingWasSent;

    /// <summary>The resource key of the icon for the failure's kind.</summary>
    public string IconKey => RequestFailureIcons.KeyFor(Model.Kind);

    public IReadOnlyList<FailureDetail> Details => Model.Details;
    public string? OriginalMessage => Model.OriginalMessage;
    public bool HasOriginalMessage => Model.OriginalMessage is not null;
    public bool HasDetails => Model.HasDetails;
    public string? ActionCaption => Model.ActionCaption;
    public bool HasActionCaption => !string.IsNullOrEmpty(Model.ActionCaption);
    public bool HasActions => Model.Actions.Count > 0;

    /// <summary>Whether the failure offers <paramref name="action"/> as a next step.</summary>
    public bool OffersAction(RequestFailureAction action) => Model.Actions.Contains(action);

    [RelayCommand]
    private Task CopyDetails() => copyText(Model.ToClipboardText());
}
