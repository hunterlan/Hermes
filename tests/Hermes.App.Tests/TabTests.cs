using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    /// <summary>The session is shared across tests, so start from an empty strip.</summary>
    private static (Views.MainWindow, MainWindowViewModel) ShowEmptyWindow()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        return (window, vm);
    }

    private static (CollectionModel Collection, HttpRequestModel First, HttpRequestModel Second) CreateRequests(string name, string requestName = "")
    {
        var collection = Workspace.CreateCollection(name + " " + Guid.NewGuid());
        var first = new HttpRequestModel { Name = requestName.Length > 0 ? requestName : "First " + Guid.NewGuid() };
        var second = new HttpRequestModel { Name = "Second " + Guid.NewGuid() };
        Workspace.AddNode(collection, null, first);
        Workspace.AddNode(collection, null, second);
        return (collection, first, second);
    }

    [AvaloniaFact]
    public void Single_click_replaces_the_preview_tab_until_it_is_edited()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Preview");
        var before = vm.Tabs.Count;

        vm.PreviewRequest(first);
        var preview = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(preview.IsPreview);

        vm.PreviewRequest(second);
        Assert.Equal(before + 1, vm.Tabs.Count);
        Assert.DoesNotContain(preview, vm.Tabs);
        var edited = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Equal(second.Id, edited.RequestId);

        edited.Url = "https://example.com/changed";
        Assert.False(edited.IsPreview);

        vm.PreviewRequest(first);
        Assert.Equal(before + 2, vm.Tabs.Count);
        Assert.Contains(edited, vm.Tabs);
        window.Close();
    }

    [AvaloniaFact]
    public void Same_named_tabs_show_their_collection()
    {
        var (window, vm) = ShowEmptyWindow();
        var shared = "Same " + Guid.NewGuid();
        var (a, firstA, _) = CreateRequests("Alpha", shared);
        var (b, firstB, _) = CreateRequests("Beta", shared);

        vm.OpenRequest(firstA);
        var tabA = vm.ActiveTab!;
        Assert.Null(tabA.TitlePrefix);

        vm.OpenRequest(firstB);
        Assert.Equal(a.Name, tabA.TitlePrefix);
        Assert.Equal(b.Name, vm.ActiveTab!.TitlePrefix);

        vm.RemoveTab(vm.ActiveTab);
        Assert.Null(tabA.TitlePrefix);
        window.Close();
    }

    [AvaloniaFact]
    public void Pinned_tabs_survive_close_all_and_move_to_the_start()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Pin");

        vm.OpenRequest(second);
        vm.OpenRequest(first);
        var pinned = vm.ActiveTab!;
        vm.TogglePinCommand.Execute(pinned);
        Assert.True(pinned.IsPinned);
        Assert.Same(pinned, vm.Tabs[0]);

        vm.CloseAllTabsCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Same(pinned, Assert.Single(vm.Tabs));
        window.Close();
    }
}
