using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Headers;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>
/// The Headers tab of a collection or folder: the editable table of headers defined here, the read-only rows
/// inherited from outer levels (folders only), how far the headers reach, and where they come from.
/// </summary>
public sealed partial class ContainerHeadersViewModel : ObservableObject
{
    private const string OverridesTagPrefix = "Overrides";
    private const string OffTagText = "Off: not sent, not passed down";
    private const string OverriddenHereTagText = "overridden here";
    private const string InheritedSection = "inherited";

    private readonly TabServices _services;
    private readonly Guid _containerId;
    private readonly HeaderRevealState _revealState;

    /// <summary>Where the container sits. <c>OuterCollection</c> is the collection above a folder, null for a collection.</summary>
    private sealed record ContainerLocation(CollectionModel Collection, CollectionModel? OuterCollection,
        IReadOnlyList<FolderModel> Outer, FolderModel? Folder);

    public ContainerHeadersViewModel(TabServices services, Guid containerId, bool isFolder)
    {
        _services = services;
        _containerId = containerId;
        IsFolder = isFolder;
        _revealState = new HeaderRevealState(services.Dialogs.CopyTextAsync);
        Headers.CopyText = services.Dialogs.CopyTextAsync;
        Headers.Changed += Refresh;
    }

    public bool IsFolder { get; }

    /// <summary>The headers defined on this collection or folder.</summary>
    public KeyValueListViewModel Headers { get; } = new() { MasksSensitiveKeys = true };

    /// <summary>Masks every revealed value again, for when the user leaves the tab.</summary>
    public void HideRevealedValues()
    {
        _revealState.Clear();
        foreach (var row in InheritedRows)
            row.Sensitive.Hide();
        Headers.HideRevealedValues();
    }

    /// <summary>Read-only rows from the levels above, outermost first. Always empty for a collection.</summary>
    public List<HeaderRowViewModel> InheritedRows { get; private set; } = [];

    public bool HasInheritedRows => InheritedRows.Count > 0;

    /// <summary>The levels from the collection down to this container, each with the headers it adds.</summary>
    public IReadOnlyList<InheritanceChainNode> Chain { get; private set; } = [];

    /// <summary>"Applies to N requests in M subfolders. K requests override X." Empty without headers defined here.</summary>
    public string UsageText { get; private set; } = "";

    public bool HasUsage => UsageText.Length > 0;

    /// <summary>The requests that override a header of this container, one line each, for "Show which".</summary>
    public IReadOnlyList<string> OverridingRequests { get; private set; } = [];

    public bool HasOverridingRequests => OverridingRequests.Count > 0;

    [ObservableProperty] public partial bool IsShowingOverridingRequests { get; set; }

    public string ShowWhichText => IsShowingOverridingRequests ? "Hide" : "Show which";

    public string IntroText => IsFolder
        ? "These headers are sent with every request in this folder and its subfolders. A subfolder or a request overrides one by using the same name."
        : "These headers are sent with every request in this collection and its folders. A folder or a request overrides one by using the same name.";

    /// <summary>The tab title: "Headers" and how many are defined here and ticked.</summary>
    public string TabTitle => $"Headers{Headers.CountBadge}";

    partial void OnIsShowingOverridingRequestsChanged(bool value) => OnPropertyChanged(nameof(ShowWhichText));

    [RelayCommand]
    private void ToggleOverridingRequests() => IsShowingOverridingRequests = !IsShowingOverridingRequests;

    /// <summary>Recomputes the tags, inherited rows, usage and chain after an edit, a save or a change elsewhere.</summary>
    public void Refresh()
    {
        try
        {
            Recompute();
        }
        catch (Exception)
        {
            Headers.SetRowTags([]);
            InheritedRows = [];
            Chain = [];
            UsageText = "";
            OverridingRequests = [];
        }
        OnPropertyChanged(nameof(TabTitle));
        OnPropertyChanged(nameof(InheritedRows));
        OnPropertyChanged(nameof(HasInheritedRows));
        OnPropertyChanged(nameof(Chain));
        OnPropertyChanged(nameof(UsageText));
        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(OverridingRequests));
        OnPropertyChanged(nameof(HasOverridingRequests));
    }

    private void Recompute()
    {
        if (Locate() is not { } location)
            throw new InvalidOperationException("The container is no longer in the workspace.");

        var ownRows = Headers.ToModels();
        var effective = ContainerHeaderResolver.Resolve(ownRows, location.Outer, location.OuterCollection);

        ShowOwnTags(ownRows, effective);
        InheritedRows = effective.Inherited.Select(CreateInheritedRow).ToList();
        // Usage and chain see the rows as edited, not as saved.
        ShowUsage(WithEditedHeaders(location, ownRows), effective.Own.Count);
        ShowChain(location, effective);
    }

    /// <summary>
    /// The location with a stand-in for the container that carries the edited rows. The real models are not touched;
    /// the stand-in shares their id, name and children.
    /// </summary>
    private static ContainerLocation WithEditedHeaders(ContainerLocation location, List<KeyValueItem> editedRows)
    {
        if (location.Folder is { } folder)
        {
            var folderStandIn = new FolderModel { Id = folder.Id, Name = folder.Name, Items = folder.Items, Headers = editedRows };
            return location with { Folder = folderStandIn };
        }
        var collection = location.Collection;
        var collectionStandIn = new CollectionModel { Id = collection.Id, Name = collection.Name, Items = collection.Items, Headers = editedRows };
        return location with { Collection = collectionStandIn };
    }

    private ContainerLocation? Locate()
    {
        if (_services.Workspace.FindCollection(_containerId) is { } collection)
            return new ContainerLocation(collection, null, [], null);
        if (_services.Workspace.Find(_containerId) is { Node: FolderModel folder } location)
            return new ContainerLocation(location.Collection, location.Collection, location.Ancestors, folder);
        return null;
    }

    /// <summary>"Overrides X" on a row that replaces an outer header, "Off: ..." on an unticked one.</summary>
    private void ShowOwnTags(List<KeyValueItem> ownRows, EffectiveHeaders effective)
    {
        // The resolved rows are the instances passed in, so rows are matched by reference.
        var resolvedByRow = effective.Own.ToDictionary<ResolvedHeader, KeyValueItem>(h => h.Item, ReferenceEqualityComparer.Instance);
        Headers.SetRowTags(ownRows.Select(row =>
        {
            if (!resolvedByRow.TryGetValue(row, out var header))
                return (!row.Enabled && row.Key.Trim().Length > 0 ? OffTagText : "", "");
            // The nearest outer level that sets the name is the last inherited row of it (outermost first).
            var overridden = header.OverridesOuter
                ? effective.Inherited.LastOrDefault(h => string.Equals(h.Key, header.Key, StringComparison.OrdinalIgnoreCase))
                : null;
            return (overridden is null ? "" : $"{OverridesTagPrefix} {overridden.Source.Name}", "");
        }).ToList());
    }

    private HeaderRowViewModel CreateInheritedRow(ResolvedHeader header)
    {
        var source = header.Source;
        return new HeaderRowViewModel(header.Key, header.Value, "")
        {
            Sensitive = _revealState.CreateValue(InheritedSection, header.Key, header.Value,
                SensitiveHeaders.IsSensitiveName(header.Key)),
            OverriddenTagText = OverriddenHereTagText,
            SourceName = source.Name,
            IsSourceCollection = source.Kind == HeaderSourceKind.Collection,
            OpenSource = () => _ = _services.OpenContainerAsync?.Invoke(source.Id),
            IsOverridden = header.State == HeaderState.Overridden,
        };
    }

    private void ShowUsage(ContainerLocation location, int activeCount)
    {
        var items = location.Folder?.Items ?? location.Collection.Items;
        var usage = ContainerHeaderUsage.Compute(_containerId, items, location.Outer, location.Collection, location.Folder);
        OverridingRequests = usage.Overriding
            .Select(o => $"{o.Request.Method} {o.Request.Name} · {string.Join(", ", o.HeaderNames)}")
            .ToList();
        if (activeCount == 0)
        {
            UsageText = "";
            return;
        }

        var text = $"Applies to {Plural(usage.RequestCount, "request")}";
        if (usage.SubfolderCount > 0)
            text += $" in {Plural(usage.SubfolderCount, "subfolder")}";
        text += ".";
        if (usage.Overriding.Count > 0)
            text += $" {Plural(usage.Overriding.Count, "request")} {(usage.Overriding.Count == 1 ? "overrides" : "override")} {string.Join(", ", usage.OverriddenNames)}.";
        UsageText = text;
    }

    /// <summary>
    /// The chain from the collection down to this container, as the auth chain on the same tab. What happens further
    /// in is told by the usage line and "Show which".
    /// </summary>
    private void ShowChain(ContainerLocation location, EffectiveHeaders effective)
    {
        var container = location.Folder is { } folder
            ? new HeaderSource(HeaderSourceKind.Folder, folder.Id, folder.Name)
            : new HeaderSource(HeaderSourceKind.Collection, location.Collection.Id, location.Collection.Name);
        Chain = HeaderInheritance.Chain(effective, location.OuterCollection, location.Outer, container);
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
