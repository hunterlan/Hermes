using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Auth;
using Hermes.Core.Headers;
using Hermes.Core.Models.Auth;

namespace Hermes.App.Tests;

public class AuthSecretFieldTests
{
    private const string Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.c2lnbmF0dXJl";

    private static AuthEditorViewModel CreateEditor(OAuth2Service oauth, AuthConfig auth)
    {
        var editor = new AuthEditorViewModel(oauth, _ => Task.FromResult(false), _ => Task.CompletedTask, allowInherit: false);
        editor.Load(auth);
        return editor;
    }

    private static AuthConfig Bearer(string token) => new() { Type = AuthType.Bearer, Bearer = { Token = token } };

    private static (Window Window, SecretVariableField Field) ShowBearerField(AuthEditorViewModel editor)
    {
        var view = new AuthEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 300 };
        window.Show();
        UiTestHelpers.Pump();
        var field = view.GetVisualDescendants().OfType<SecretVariableField>().Single(field => field.IsEffectivelyVisible);
        return (window, field);
    }

    private static TextBlock Dots(SecretVariableField field) =>
        field.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("maskDots"));

    private static VariableTextBox Box(SecretVariableField field) => field.GetVisualDescendants().OfType<VariableTextBox>().Single();

    [AvaloniaFact]
    public void Typed_credentials_are_masked_and_variable_references_are_not()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, new AuthConfig
        {
            Type = AuthType.ApiKey,
            Bearer = { Token = Token },
            ApiKey = { Key = "X-Token", Value = "{{apiKey}}" },
            OAuth2 = { AccessToken = "" },
        });

        Assert.True(editor.BearerTokenSecret.ShowMask);
        Assert.Equal(SensitiveHeaders.MaskText, editor.BearerTokenSecret.DisplayValue);
        Assert.False(editor.ApiKeyValueSecret.IsSensitive);
        Assert.Equal("{{apiKey}}", editor.ApiKeyValueSecret.DisplayValue);
        Assert.False(editor.AccessTokenSecret.ShowMask);

        editor.ApiKeyValue = "Bearer {{apiKey}}";
        Assert.True(editor.ApiKeyValueSecret.ShowMask);
    }

    [AvaloniaFact]
    public void Leaving_the_tab_masks_a_revealed_token_again()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.Auth.Load(Bearer(Token));
            tab.Auth.BearerTokenSecret.ToggleRevealCommand.Execute(null);
            Assert.False(tab.Auth.BearerTokenSecret.ShowMask);

            tab.IsActive = false;

            Assert.True(tab.Auth.BearerTokenSecret.ShowMask);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_bearer_field_shows_dots_reveals_while_focused_and_keeps_its_frame()
    {
        using var oauth = new OAuth2Service();
        var (window, field) = ShowBearerField(CreateEditor(oauth, Bearer(Token)));

        Assert.True(Dots(field).IsVisible);
        Assert.Equal(0, Box(field).Opacity);
        var frame = field.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("inputFrame"));
        Assert.Equal(1, frame.Opacity);
        Assert.Equal(1, frame.BorderThickness.Left);

        Box(field).FocusEditor();
        UiTestHelpers.Pump();

        Assert.False(Dots(field).IsVisible);
        Assert.Equal(1, Box(field).Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void The_eye_reveals_the_bearer_token()
    {
        using var oauth = new OAuth2Service();
        var editor = CreateEditor(oauth, Bearer(Token));
        var (window, field) = ShowBearerField(editor);
        var eye = field.GetVisualDescendants().OfType<Button>().Single();
        Assert.True(eye.IsVisible);

        eye.Command!.Execute(null);
        UiTestHelpers.Pump();

        Assert.True(editor.BearerTokenSecret.IsRevealed);
        Assert.False(Dots(field).IsVisible);
        Assert.Equal(1, Box(field).Opacity);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Screenshot_of_the_masked_bearer_field(string theme)
    {
        using var oauth = new OAuth2Service();
        var (window, _) = ShowBearerField(CreateEditor(oauth, Bearer(Token)));
        window.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        UiTestHelpers.Pump();
        UiTestHelpers.Screenshot(window, $"72-sensitive-auth-bearer-{theme}");
        window.Close();
    }
}
