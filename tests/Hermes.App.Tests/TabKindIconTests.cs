using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabKindIconTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public async Task Each_non_request_tab_exposes_its_icon_key_and_name()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var collection = Workspace.CreateCollection("Kind icons " + Guid.NewGuid());
            var folder = new FolderModel { Name = "Folder " + Guid.NewGuid() };
            Workspace.AddNode(collection, null, folder);
            var environment = new EnvironmentModel { Name = "Env " + Guid.NewGuid() };
            Workspace.Environments.Add(environment);

            var requestTab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            Assert.Null(requestTab.KindIconKey);
            Assert.Null(requestTab.KindName);

            await vm.OpenCollectionAsync(collection);
            AssertKind(vm.ActiveTab, "IconCollections", "Collection");
            await vm.OpenFolderAsync(folder);
            AssertKind(vm.ActiveTab, "IconFolderOutline", "Folder");
            await vm.OpenEnvironmentAsync(environment);
            AssertKind(vm.ActiveTab, "IconEnvironment", "Environment");
            await vm.OpenEnvironmentAsync(Workspace.Globals);
            AssertKind(vm.ActiveTab, "IconGlobals", "Globals");
            await vm.OpenEnvironmentMatrixAsync();
            AssertKind(vm.ActiveTab, "IconTable", "Environment matrix");
            await vm.OpenManualAsync();
            AssertKind(vm.ActiveTab, "IconHelp", "Manual");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Non_request_tab_headers_show_an_icon_and_no_text_label()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            await vm.OpenEnvironmentAsync(Workspace.Globals);
            await vm.OpenEnvironmentMatrixAsync();
            UiTestHelpers.Pump();

            var requestHeader = HeaderOf(window, vm.Tabs.First(tab => tab is RequestTabViewModel));
            Assert.True(requestHeader.GetVisualDescendants().OfType<MethodBadge>().Single().IsVisible);
            Assert.DoesNotContain(KindIcons(requestHeader), icon => icon.IsVisible);

            AssertIconOnly(HeaderOf(window, vm.Tabs.OfType<EnvironmentTabViewModel>().Single()), "Globals");
            AssertIconOnly(HeaderOf(window, vm.Tabs.OfType<EnvironmentMatrixTabViewModel>().Single()), "Environment matrix");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Tab_strip_with_every_kind_of_tab(string themeName)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        window.RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            var collection = Workspace.CreateCollection("Strip " + Guid.NewGuid());
            var folder = new FolderModel { Name = "Users" };
            Workspace.AddNode(collection, null, folder);
            await vm.OpenCollectionAsync(collection);
            await vm.OpenFolderAsync(folder);
            await vm.OpenEnvironmentAsync(Workspace.Globals);
            await vm.OpenEnvironmentMatrixAsync();
            await vm.OpenManualAsync();
            UiTestHelpers.Pump();

            foreach (var tab in vm.Tabs.Where(tab => tab.KindIconKey is not null))
            {
                var header = HeaderOf(window, tab);
                var icon = KindIcons(header).Single(candidate => candidate.IsVisible);
                var title = header.FindControl<TextBlock>("TitleText")!;
                Assert.Equal(CenterY(title, header), CenterY(icon, header), 1.5);
            }

            UiTestHelpers.Screenshot(window, "kind-icons-tab-strip-" + themeName.ToLowerInvariant());
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertKind(TabViewModelBase? tab, string expectedKey, string expectedName)
    {
        Assert.NotNull(tab);
        Assert.Equal(expectedKey, tab.KindIconKey);
        Assert.Equal(expectedName, tab.KindName);
    }

    private static void AssertIconOnly(TabHeader header, string expectedName)
    {
        var icon = KindIcons(header).Single(candidate => candidate.IsVisible);
        Assert.Equal(expectedName, Avalonia.Automation.AutomationProperties.GetName(icon));
        Assert.NotNull(icon.Data);
        Assert.DoesNotContain(header.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text is "COL" or "DIR" or "ENV" or "GLB" or "HELP");
    }

    private static IEnumerable<PathIcon> KindIcons(TabHeader header) =>
        header.GetVisualDescendants().OfType<PathIcon>().Where(icon => icon.Parent is Panel && icon.Width == 12 && icon.Height == 12);

    private static TabHeader HeaderOf(Window window, TabViewModelBase tab) =>
        window.GetVisualDescendants().OfType<TabHeader>().Single(header => ReferenceEquals(header.DataContext, tab));

    private static double CenterY(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Avalonia.Point(0, control.Bounds.Height / 2), relativeTo)!.Value.Y;
}
