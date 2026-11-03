using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;

namespace Hermes.App.Controls;

/// <summary>
/// Shows the active tab in a small pool of views. The views of the most recently used tabs stay alive, hidden, so
/// switching back to one is instant: its documents aren't copied into a shared view and highlighted again. When the
/// pool is full, a tab without a view takes over the least recently used one, so it costs what a shared view costs
/// and memory stays bounded however many tabs are open. Views come from the <see cref="Control.DataTemplates"/> of
/// this host; with <see cref="MaxLiveViews"/> at 1, one view is reused for every tab.
/// </summary>
public sealed class TabViewHost : Panel
{
    public static readonly StyledProperty<object?> ActiveTabProperty =
        AvaloniaProperty.Register<TabViewHost, object?>(nameof(ActiveTab));

    public static readonly StyledProperty<IEnumerable?> TabsProperty =
        AvaloniaProperty.Register<TabViewHost, IEnumerable?>(nameof(Tabs));

    public static readonly StyledProperty<int> MaxLiveViewsProperty =
        AvaloniaProperty.Register<TabViewHost, int>(nameof(MaxLiveViews), 3, coerce: (_, value) => Math.Max(1, value));

    // Most recently used first; each entry hosts one tab's view.
    private readonly List<(object Tab, ContentControl Host)> _liveViews = [];
    private INotifyCollectionChanged? _observedTabs;

    public object? ActiveTab
    {
        get => GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    /// <summary>The open tabs; a view is dropped as soon as its tab closes.</summary>
    public IEnumerable? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    /// <summary>How many tab views the pool holds, the active one included.</summary>
    public int MaxLiveViews
    {
        get => GetValue(MaxLiveViewsProperty);
        set => SetValue(MaxLiveViewsProperty, value);
    }

    /// <summary>The number of tab views currently alive, for tests.</summary>
    internal int LiveViewCount => _liveViews.Count;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveTabProperty)
            ShowActiveTab();
        else if (change.Property == TabsProperty)
            ObserveTabs(VisualRoot is null ? null : change.GetNewValue<IEnumerable?>());
        else if (change.Property == MaxLiveViewsProperty)
            TrimToLimit();
    }

    // The tabs collection outlives this host, so it is only observed while the host is in a window.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveTabs(Tabs);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ObserveTabs(null);
    }

    private void ShowActiveTab()
    {
        var activeTab = ActiveTab;
        if (activeTab is null)
        {
            foreach (var (_, host) in _liveViews)
                host.IsVisible = false;
            return;
        }

        var index = _liveViews.FindIndex(view => ReferenceEquals(view.Tab, activeTab));
        ContentControl activeHost;
        if (index >= 0)
        {
            activeHost = _liveViews[index].Host;
            _liveViews.RemoveAt(index);
        }
        else if (_liveViews.Count >= MaxLiveViews)
        {
            // The pool is full: take over the least recently used view. Its template is kept when the new tab is of
            // the same kind, so this costs what switching a single shared view costs, not building a new view.
            activeHost = _liveViews[^1].Host;
            _liveViews.RemoveAt(_liveViews.Count - 1);
            activeHost.Content = activeTab;
        }
        else
        {
            activeHost = new ContentControl { Content = activeTab };
            Children.Add(activeHost);
        }
        _liveViews.Insert(0, (activeTab, activeHost));

        // Show the new view before hiding the old one, so focus has somewhere to go.
        activeHost.IsVisible = true;
        foreach (var (_, host) in _liveViews.Skip(1))
            host.IsVisible = false;
        TrimToLimit();
    }

    private void TrimToLimit()
    {
        while (_liveViews.Count > MaxLiveViews)
            RemoveView(_liveViews.Count - 1);
    }

    private void RemoveView(int index)
    {
        var host = _liveViews[index].Host;
        _liveViews.RemoveAt(index);
        Children.Remove(host);
        host.Content = null;
    }

    private void ObserveTabs(IEnumerable? tabs)
    {
        if (_observedTabs is not null)
            _observedTabs.CollectionChanged -= OnTabsChanged;
        _observedTabs = tabs as INotifyCollectionChanged;
        if (_observedTabs is null)
            return;
        _observedTabs.CollectionChanged += OnTabsChanged;
        DropClosedTabs();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => DropClosedTabs();

    private void DropClosedTabs()
    {
        var openTabs = Tabs?.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance) ?? [];
        for (var index = _liveViews.Count - 1; index >= 0; index--)
        {
            if (!openTabs.Contains(_liveViews[index].Tab))
                RemoveView(index);
        }
    }
}
