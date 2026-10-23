using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.Core.Variables;

namespace Hermes.App.Tests;

public class VariableSuggestionTests
{
    private static (Window Window, VariableTextBox Box) ShowBox(string text = "")
    {
        var box = new VariableTextBox
        {
            Text = text,
            Variables = new VariableScope().Set("baseUrl", "https://api.test").Set("basePath", "/v1").Set("token", "t"),
        };
        var window = new Window { Content = box, Width = 600, Height = 200 };
        window.Show();
        box.FocusEditor();
        UiTestHelpers.Pump();
        return (window, box);
    }

    [AvaloniaFact]
    public void Typing_braces_lists_matching_variables_and_enter_completes_them()
    {
        var (window, box) = ShowBox();

        window.KeyTextInput("{{ba");
        UiTestHelpers.Pump();
        Assert.True(box.IsSuggestionListOpen);
        Assert.Equal(["basePath", "baseUrl"], box.SuggestionNames);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Equal("{{baseUrl}}", box.Text);
        Assert.False(box.IsSuggestionListOpen);

        window.KeyTextInput("/users");
        Assert.Equal("{{baseUrl}}/users", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_closes_the_list_and_unknown_names_show_nothing()
    {
        var (window, box) = ShowBox();

        window.KeyTextInput("{{");
        UiTestHelpers.Pump();
        Assert.True(box.IsSuggestionListOpen);
        Assert.Equal(5, box.SuggestionNames.Count);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(box.IsSuggestionListOpen);

        window.KeyTextInput("zzz");
        UiTestHelpers.Pump();
        Assert.False(box.IsSuggestionListOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void Url_bar_suggests_variables_from_the_active_scope()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<ViewModels.RequestTabViewModel>(vm.ActiveTab);
        tab.Variables = new VariableScope().Set("baseUrl", "https://api.example.com").Set("userId", "42");
        var url = window.GetVisualDescendants().OfType<VariableTextBox>().First(b => b.Name == "UrlBox");
        url.FocusEditor();
        UiTestHelpers.Pump();

        window.KeyTextInput("{{");
        UiTestHelpers.Pump();
        Assert.True(url.IsSuggestionListOpen);
        Assert.Equal(["baseUrl", "userId"], url.SuggestionNames.Take(2));
        UiTestHelpers.Screenshot(window, "22-variable-suggestions");
        window.Close();
    }
}
