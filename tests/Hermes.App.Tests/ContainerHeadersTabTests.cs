using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The Headers tab of collections and folders.</summary>
public class ContainerHeadersTabTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    private static void AddHeader(KeyValueListViewModel headers, string key, string value, bool enabled = true)
    {
        var row = headers.Rows.Last(r => r.IsPlaceholder);
        row.Key = key;
        row.Value = value;
        row.Enabled = enabled;
    }

    private static (CollectionModel Collection, FolderModel Outer, FolderModel Inner, HttpRequestModel Request) CreateTree()
    {
        var collection = Workspace.CreateCollection("Headers " + Guid.NewGuid());
        collection.Headers = [new KeyValueItem("X-Api-Version", "1"), new KeyValueItem("Accept", "*/*")];
        var outer = new FolderModel { Name = "Users" };
        var inner = new FolderModel { Name = "Admin" };
        var request = new HttpRequestModel { Name = "List", Url = "http://localhost/users" };
        Workspace.AddNode(collection, null, outer);
        Workspace.AddNode(collection, outer, inner);
        Workspace.AddNode(collection, inner, request);
        Workspace.SaveCollection(collection);
        return (collection, outer, inner, request);
    }

    [AvaloniaFact]
    public async Task Folder_headers_are_saved_counted_and_sent_by_requests_inside()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, _, request) = CreateTree();
        try
        {
            await vm.OpenFolderAsync(outer);
            var tab = Assert.IsType<FolderTabViewModel>(vm.ActiveTab);
            Assert.Equal("Headers", tab.HeadersEditor.TabTitle);

            AddHeader(tab.HeadersEditor.Headers, "X-Team", "core");
            UiTestHelpers.Pump();
            Assert.True(tab.IsDirty);
            Assert.Equal("Headers (1)", tab.HeadersEditor.TabTitle);

            await tab.SaveAsync();
            Assert.False(tab.IsDirty);
            Assert.Contains(outer.Headers, h => h.Key == "X-Team");

            var sent = Workspace.CreateContext(request.Id).ResolveHeaders(request).HeadersToSend.Select(h => h.Key);
            Assert.Contains("X-Team", sent);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Rows_are_tagged_when_they_override_or_are_off()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, _, _) = CreateTree();
        try
        {
            await vm.OpenFolderAsync(outer);
            var headers = Assert.IsType<FolderTabViewModel>(vm.ActiveTab).HeadersEditor;
            AddHeader(headers.Headers, "accept", "text/plain");
            AddHeader(headers.Headers, "X-Off", "1", enabled: false);
            UiTestHelpers.Pump();

            var rows = headers.Headers.Rows.Where(r => !r.IsPlaceholder).ToList();
            Assert.Equal($"Overrides {collection.Name}", rows[0].TagText);
            Assert.Equal("Off: not sent, not passed down", rows[1].TagText);

            var overridden = Assert.Single(headers.InheritedRows, r => r.IsOverridden);
            Assert.Equal("Accept", overridden.Key);
            Assert.Equal("overridden here", overridden.StatusText);
            Assert.Equal(2, headers.InheritedRows.Count);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Usage_line_and_chain_describe_the_reach_and_the_sources()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, inner, request) = CreateTree();
        outer.Headers = [new KeyValueItem("X-Outer", "1")];
        inner.Headers = [new KeyValueItem("X-Team", "inner")];
        request.Headers = [new KeyValueItem("x-team", "mine")];
        Workspace.SaveCollection(collection);
        try
        {
            await vm.OpenFolderAsync(inner);
            var headers = Assert.IsType<FolderTabViewModel>(vm.ActiveTab).HeadersEditor;
            Assert.Equal("Applies to 1 request. 1 request overrides X-Team.", headers.UsageText);
            Assert.Single(headers.OverridingRequests);

            // The chain ends at the open folder; requests further in are covered by the usage line.
            Assert.Equal(["Collection", "Folder · Users", "Folder · Admin"],
                headers.Chain.Select(n => n.Title.StartsWith("Collection") ? "Collection" : n.Title));
            Assert.True(headers.Chain[2].IsSource);
            Assert.True(headers.Chain[2].IsLast);
            Assert.False(headers.Chain[1].IsSource);
            Assert.Contains(headers.Chain[1].Details, d => d.Text == "X-Outer" && !d.IsStruckThrough);
            Assert.Contains(headers.Chain[2].Details, d => d.Text == "X-Team" && !d.IsStruckThrough);

            await vm.OpenFolderAsync(outer);
            var outerHeaders = Assert.IsType<FolderTabViewModel>(vm.ActiveTab).HeadersEditor;
            Assert.Equal("Applies to 1 request in 1 subfolder.", outerHeaders.UsageText);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Chain_and_usage_follow_unsaved_edits_without_touching_the_model()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, _, request) = CreateTree();
        try
        {
            await vm.OpenFolderAsync(outer);
            var tab = Assert.IsType<FolderTabViewModel>(vm.ActiveTab);
            Assert.False(tab.HeadersEditor.HasUsage);

            AddHeader(tab.HeadersEditor.Headers, "X-Draft", "1");
            request.Headers = [new KeyValueItem("x-draft", "mine")];
            tab.HeadersEditor.Refresh();

            Assert.Empty(outer.Headers);
            Assert.Equal("Applies to 1 request in 1 subfolder. 1 request overrides X-Draft.", tab.HeadersEditor.UsageText);
            var folderNode = tab.HeadersEditor.Chain.Single(n => n.Title == "Folder · Users");
            Assert.Contains(folderNode.Details, d => d.Text == "X-Draft" && !d.IsStruckThrough);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Chain_strikes_through_outer_headers_the_folder_overrides()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, _, _) = CreateTree();
        outer.Headers = [new KeyValueItem("accept", "text/plain")];
        Workspace.SaveCollection(collection);
        try
        {
            await vm.OpenFolderAsync(outer);
            var chain = Assert.IsType<FolderTabViewModel>(vm.ActiveTab).HeadersEditor.Chain;
            Assert.Equal(2, chain.Count);
            Assert.Contains(chain[0].Details, d => d.Text == "Accept" && d.IsStruckThrough);
            Assert.Contains(chain[0].Details, d => d.Text == "X-Api-Version" && !d.IsStruckThrough);
            Assert.Contains(chain[1].Details, d => d.Text == "accept" && !d.IsStruckThrough);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Inherited_rows_refresh_when_an_outer_level_changes()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, outer, _, _) = CreateTree();
        try
        {
            await vm.OpenFolderAsync(outer);
            var tab = Assert.IsType<FolderTabViewModel>(vm.ActiveTab);
            Assert.Equal(2, tab.HeadersEditor.InheritedRows.Count);

            collection.Headers.Add(new KeyValueItem("X-New", "1"));
            tab.OnWorkspaceChanged();
            Assert.Contains(tab.HeadersEditor.InheritedRows, r => r.Key == "X-New" && r.SourceName == collection.Name && r.IsSourceCollection);
            Assert.Contains(tab.HeadersEditor.Chain[0].Details, d => d.Text == "X-New");
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Collection_headers_are_saved_and_reach_requests()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (collection, _, _, request) = CreateTree();
        try
        {
            await vm.OpenCollectionAsync(collection);
            var tab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);
            Assert.Equal("Headers (2)", tab.HeadersEditor.TabTitle);
            Assert.Empty(tab.HeadersEditor.InheritedRows);
            Assert.Contains("collection", tab.HeadersEditor.IntroText);

            AddHeader(tab.HeadersEditor.Headers, "X-Extra", "1");
            Assert.True(tab.IsDirty);
            await tab.SaveAsync();
            Assert.False(tab.IsDirty);
            Assert.Contains(Workspace.CreateContext(request.Id).ResolveHeaders(request).HeadersToSend, h => h.Key == "X-Extra");
            Assert.StartsWith("Applies to 1 request in 2 subfolders.", tab.HeadersEditor.UsageText);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Headers_tabs_render_in_both_themes(string themeName)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        window.RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var (collection, outer, inner, request) = CreateTree();
        inner.Headers = [new KeyValueItem("accept", "text/plain"), new KeyValueItem("X-Off", "1") { Enabled = false }];
        request.Headers = [new KeyValueItem("Accept", "application/json")];
        Workspace.SaveCollection(collection);
        try
        {
            await vm.OpenFolderAsync(inner);
            SelectHeadersTab(window);
            Assert.Contains(window.GetVisualDescendants().OfType<ContainerHeadersSection>(), s => s.IsEffectivelyVisible);
            UiTestHelpers.Screenshot(window, $"folder-headers-{themeName}");

            await vm.OpenCollectionAsync(collection);
            SelectHeadersTab(window);
            UiTestHelpers.Screenshot(window, $"collection-headers-{themeName}");
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    /// <summary>A very long source name must not widen the Key column: every row's Value cell starts at the same x.</summary>
    [AvaloniaTheory]
    [InlineData("Light", 1400)]
    [InlineData("Dark", 1400)]
    [InlineData("Light", 700)]
    [InlineData("Dark", 700)]
    public async Task Long_tags_do_not_shift_the_table_columns(string themeName, int windowWidth)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        window.Width = windowWidth;
        window.RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var collection = Workspace.CreateCollection("A very long collection name " + new string('x', 80));
        collection.Headers = [new KeyValueItem("Accept", "*/*")];
        var folder = new FolderModel { Name = "Users" };
        var request = new HttpRequestModel { Name = "List", Url = "http://localhost/users" };
        request.Headers = [new KeyValueItem("accept", "text/plain"), new KeyValueItem("X-Short", "1")];
        Workspace.AddNode(collection, null, folder);
        Workspace.AddNode(collection, folder, request);
        folder.Headers = [new KeyValueItem("accept", "x"), new KeyValueItem("X-Short", "1")];
        Workspace.SaveCollection(collection);
        try
        {
            await vm.OpenFolderAsync(folder);
            SelectHeadersTab(window);
            AssertValueColumnsAligned(window);
            UiTestHelpers.Screenshot(window, $"folder-headers-long-tag-{themeName}-{windowWidth}");

            await vm.OpenRequestAsync(request);
            UiTestHelpers.Pump();
            AssertValueColumnsAligned(window);
            UiTestHelpers.Screenshot(window, $"request-headers-long-tag-{themeName}-{windowWidth}");
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    private static void AssertValueColumnsAligned(Window window)
    {
        var table = window.GetVisualDescendants().OfType<KeyValueTable>()
            .First(t => t.IsEffectivelyVisible && t.DataContext is KeyValueListViewModel { Rows.Count: > 2 });
        var starts = table.GetVisualDescendants().OfType<Hermes.App.Controls.VariableTextBox>()
            .Where(b => b.IsEffectivelyVisible)
            .Select(b => Math.Round(Avalonia.VisualExtensions.TranslatePoint(b, new Avalonia.Point(0, 0), table)!.Value.X))
            .Distinct().ToList();
        // The key stays readable however long the tags are.
        var keyBoxes = table.GetVisualDescendants().OfType<TextBox>()
            .Where(b => b.IsEffectivelyVisible && b.PlaceholderText == "Key");
        Assert.All(keyBoxes, box => Assert.True(box.Bounds.Width >= 80, $"Key box is {box.Bounds.Width}px wide"));
        Assert.True(starts.Count == 1, "Value cells start at " + string.Join(", ", starts));
    }

    private static void SelectHeadersTab(Window window)
    {
        UiTestHelpers.Pump();
        var tabs = window.GetVisualDescendants().OfType<TabControl>()
            .First(t => t.IsEffectivelyVisible && t.Items.OfType<TabItem>().Any(i => i.Header is string s && s.StartsWith("Headers")));
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(i => i.Header is string s && s.StartsWith("Headers"));
        UiTestHelpers.Pump();
    }
}
