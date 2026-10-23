using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class RenameTests
{
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
