using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The header shared by collection, folder and environment tabs.</summary>
public class ContainerTabHeaderTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public async Task Renaming_a_collection_in_its_header_updates_the_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = Workspace.CreateCollection("Header " + Guid.NewGuid());
        try
        {
            await vm.OpenCollectionAsync(collection);
            var tab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();

            var header = Assert.Single(window.GetVisualDescendants().OfType<ContainerTabHeader>());
            var nameBox = header.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal(tab.Name, nameBox.Text);
            nameBox.Text = "Renamed";
            // The box pushes its text to the binding when focus leaves, as the hand-built header did.
            nameBox.Focus();
            header.Focus();
            UiTestHelpers.Pump();
            Assert.Equal("Renamed", tab.Name);
            Assert.Same(tab.SaveCommand, header.SaveCommand);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Folder_tab_uses_the_shared_header()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = Workspace.CreateCollection("Header " + Guid.NewGuid());
        var folder = new FolderModel { Name = "Users" };
        Workspace.AddNode(collection, null, folder);
        try
        {
            await vm.OpenFolderAsync(folder);
            var tab = Assert.IsType<FolderTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();

            var header = Assert.Single(window.GetVisualDescendants().OfType<ContainerTabHeader>());
            Assert.Equal("Users", header.Title);
            Assert.Same(tab.SaveCommand, header.SaveCommand);
            Assert.Empty(header.Actions);
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Authorization_section_names_the_container_kind()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = Workspace.CreateCollection("Header " + Guid.NewGuid());
        var folder = new FolderModel { Name = "Users" };
        Workspace.AddNode(collection, null, folder);
        try
        {
            await vm.OpenCollectionAsync(collection);
            ((CollectionTabViewModel)vm.ActiveTab!).SelectedSection = 1;
            UiTestHelpers.Pump();
            Assert.Contains(AuthorizationTexts(window), text => text.EndsWith("in this collection that inherits auth from its parent."));

            await vm.OpenFolderAsync(folder);
            ((FolderTabViewModel)vm.ActiveTab!).SelectedSection = 1;
            UiTestHelpers.Pump();
            Assert.Contains(AuthorizationTexts(window), text => text.EndsWith("in this folder that inherits auth from its parent."));
        }
        finally
        {
            Workspace.DeleteCollection(collection);
            window.Close();
        }
    }

    private static IEnumerable<string> AuthorizationTexts(Window window) =>
        window.GetVisualDescendants().OfType<ContainerAuthorizationSection>().Where(section => section.IsEffectivelyVisible)
            .SelectMany(section => section.GetVisualDescendants().OfType<TextBlock>())
            .Select(text => text.Text ?? "");

    [AvaloniaFact]
    public async Task Environment_header_puts_Set_active_before_Save()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var environment = Workspace.CreateEnvironment("Header " + Guid.NewGuid());
        try
        {
            await vm.OpenEnvironmentAsync(environment);
            var tab = Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();

            var header = Assert.Single(window.GetVisualDescendants().OfType<ContainerTabHeader>());
            var setActive = Assert.IsType<Button>(Assert.Single(header.Actions));
            Assert.Equal("Set active", setActive.Content);
            Assert.Same(tab.SetActiveCommand, setActive.Command);
            var save = header.GetVisualDescendants().OfType<Button>().Single(button => button.Command == tab.SaveCommand);
            var setActiveRight = setActive.TranslatePoint(new Point(setActive.Bounds.Width, 0), header)!.Value.X;
            var saveLeft = save.TranslatePoint(new Point(0, 0), header)!.Value.X;
            Assert.Equal(6, saveLeft - setActiveRight);
            Assert.True(header.GetVisualDescendants().OfType<TextBox>().Single().IsEffectivelyVisible);
        }
        finally
        {
            Workspace.DeleteEnvironment(environment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Globals_header_shows_a_fixed_title_without_Set_active()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            await vm.OpenEnvironmentAsync(Workspace.Globals);
            UiTestHelpers.Pump();

            var header = Assert.Single(window.GetVisualDescendants().OfType<ContainerTabHeader>());
            Assert.False(header.GetVisualDescendants().OfType<TextBox>().Single().IsEffectivelyVisible);
            var title = header.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Globals");
            Assert.True(title.IsEffectivelyVisible);
            var setActive = header.Actions.OfType<Button>().First();
            Assert.False(setActive.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
