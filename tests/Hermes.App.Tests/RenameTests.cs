using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class RenameTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    private static InlineRenameBox VisibleRenameBox(Window window) =>
        window.GetVisualDescendants().OfType<InlineRenameBox>().Single(box => box.IsEffectivelyVisible);

    private static void TypeRequestName(Window window, RequestTabViewModel tab, string name)
    {
        tab.StartRenameCommand.Execute(null);
        UiTestHelpers.Pump();
        UiTestHelpers.Pump();
        Assert.True(VisibleRenameBox(window).IsFocused);
        window.KeyTextInput(name);
    }

    [AvaloniaFact]
    public void Enter_commits_the_request_name_edit()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        TypeRequestName(window, tab, "Get users");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.False(tab.IsRenaming);
        Assert.Equal("Get users", tab.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Losing_focus_commits_the_request_name_edit()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        TypeRequestName(window, tab, "Get orders");
        window.GetVisualDescendants().OfType<Button>().First(button => button.Focusable && button.IsEffectivelyVisible).Focus();
        UiTestHelpers.Pump();

        Assert.False(tab.IsRenaming);
        Assert.Equal("Get orders", tab.Name);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_request_name_becomes_untitled(string blankName)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        tab.StartRenameCommand.Execute(null);
        UiTestHelpers.Pump();
        UiTestHelpers.Pump();
        var box = VisibleRenameBox(window);
        box.Text = blankName;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.Equal("Untitled Request", tab.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_the_request_name_edit()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        var originalName = tab.Name;

        tab.StartRenameCommand.Execute(null);
        UiTestHelpers.Pump();
        UiTestHelpers.Pump();
        Assert.True(VisibleRenameBox(window).IsFocused);
        window.KeyTextInput("Discarded");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();

        Assert.False(tab.IsRenaming);
        Assert.Equal(originalName, tab.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Collection_rename_commits_on_Enter_and_cancels_on_Escape()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.SelectedPane = SidebarPane.Collections;
        var originalName = "Rename " + Guid.NewGuid();
        var collection = Workspace.CreateCollection(originalName);
        var node = vm.Collections.Find(collection.Id)!;

        vm.Collections.RenameCommand.Execute(node);
        UiTestHelpers.Pump();
        UiTestHelpers.Pump();
        Assert.True(VisibleRenameBox(window).IsFocused);
        window.KeyTextInput("Discarded");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.False(node.IsRenaming);
        Assert.Equal(originalName, collection.Name);

        vm.Collections.RenameCommand.Execute(vm.Collections.Find(collection.Id));
        UiTestHelpers.Pump();
        window.KeyTextInput(originalName + " v2");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Equal(originalName + " v2", collection.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Environment_rename_commits_on_Enter_and_cancels_on_Escape()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.SelectedPane = SidebarPane.Environments;
        var originalName = "Rename " + Guid.NewGuid();
        var environment = Workspace.CreateEnvironment(originalName);
        var item = vm.Environments.Items.Single(candidate => candidate.Model == environment);

        vm.Environments.RenameCommand.Execute(item);
        UiTestHelpers.Pump();
        UiTestHelpers.Pump();
        Assert.True(VisibleRenameBox(window).IsFocused);
        window.KeyTextInput("Discarded");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Equal(originalName, environment.Name);

        vm.Environments.RenameCommand.Execute(vm.Environments.Items.Single(candidate => candidate.Model == environment));
        UiTestHelpers.Pump();
        window.KeyTextInput(originalName + " v2");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Equal(originalName + " v2", environment.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_request_name_lets_you_type_a_new_one()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        var nameButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Command == tab.StartRenameCommand);
        var point = nameButton.TranslatePoint(new Point(nameButton.Bounds.Width / 2, nameButton.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.True(tab.IsRenaming);
        var nameBox = window.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "NameBox");
        Assert.True(nameBox.IsFocused);

        window.KeyTextInput("Get users");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Equal("Get users", tab.Name);
        Assert.False(tab.IsRenaming);
        window.Close();
    }
}
