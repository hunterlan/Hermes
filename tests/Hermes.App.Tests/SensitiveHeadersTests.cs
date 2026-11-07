using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class SensitiveHeadersTests
{
    private const string Jwt = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkFkYSBMb3ZlbGFjZSJ9.c2lnbmF0dXJl";

    private static (Window Window, HeaderListView List, List<string> Copied, HeaderRowViewModel Token, HeaderRowViewModel Plain) ShowList()
    {
        var copied = new List<string>();
        Task CopyAsync(string text)
        {
            copied.Add(text);
            return Task.CompletedTask;
        }

        var token = new HeaderRowViewModel("Authorization", Jwt, "from auth")
        {
            Sensitive = new SensitiveValueViewModel(Jwt, isSensitive: true, CopyAsync) { CopiedDuration = TimeSpan.FromMilliseconds(30) },
        };
        var plain = new HeaderRowViewModel("Accept", "application/json", "added automatically");
        var list = new HeaderListView { Rows = new[] { token, plain } };
        var window = new Window { Content = list, Width = 720, Height = 420 };
        window.Show();
        UiTestHelpers.Pump();
        return (window, list, copied, token, plain);
    }

    private static IEnumerable<TextBlock> ValueTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "ValueText");

    private static Button ButtonNamed(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().First(button =>
            Avalonia.Automation.AutomationProperties.GetName(button) == name && button.IsVisible);

    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    /// <summary>A saved request with a Bearer token, a custom-named API key header and a hand-typed Cookie header.</summary>
    private static async Task<RequestTabViewModel> OpenRequestWithCredentialsAsync(MainWindowViewModel vm, AuthConfig auth)
    {
        var collection = Workspace.CreateCollection("Secrets " + Guid.NewGuid());
        var request = new HttpRequestModel
        {
            Name = "Secret",
            Url = "https://example.test/secret",
            Headers = [new("Cookie", "session=abc123")],
            Auth = auth,
        };
        Workspace.AddNode(collection, null, request);
        Workspace.SaveCollection(collection);
        await vm.OpenRequestAsync(request);
        UiTestHelpers.Pump();
        return Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
    }

    private static AuthConfig BearerAuth() => new() { Type = AuthType.Bearer, Bearer = { Token = Jwt["Bearer ".Length..] } };

    [AvaloniaFact]
    public async Task Auth_headers_are_masked_whatever_their_name()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var tab = await OpenRequestWithCredentialsAsync(vm,
                new AuthConfig { Type = AuthType.ApiKey, ApiKey = { Key = "X-Custom-Token", Value = "secret-value" } });

            var customRow = tab.HeadersEditor.AutomaticRows.Single(row => row.Key == "X-Custom-Token");
            Assert.True(customRow.Sensitive.IsSensitive);
            Assert.Equal(SensitiveHeaders.MaskText, customRow.Sensitive.DisplayValue);
            Assert.True(tab.HeadersEditor.Headers.Rows[0].Sensitive.IsSensitive);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Reveal_survives_an_edit_and_resets_when_the_tab_becomes_inactive()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var tab = await OpenRequestWithCredentialsAsync(vm, BearerAuth());
            tab.HeadersEditor.AutomaticRows.Single(row => row.Key == "Authorization").Sensitive.ToggleRevealCommand.Execute(null);

            // Editing anything rebuilds the rows.
            tab.HeadersEditor.Headers.Rows[^1].Key = "X-Edit";
            UiTestHelpers.Pump();

            var rebuilt = tab.HeadersEditor.AutomaticRows.Single(row => row.Key == "Authorization");
            Assert.True(rebuilt.Sensitive.IsRevealed);
            Assert.Equal(Jwt, rebuilt.Sensitive.DisplayValue);

            tab.IsActive = true;
            tab.IsActive = false;

            Assert.True(tab.HeadersEditor.AutomaticRows.Single(row => row.Key == "Authorization").Sensitive.ShowMask);
            tab.HeadersEditor.Headers.Rows[^1].Key = "X-Edit-Again";
            Assert.True(tab.HeadersEditor.AutomaticRows.Single(row => row.Key == "Authorization").Sensitive.ShowMask);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_real_token_on_the_clipboard_while_the_row_is_masked()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var tab = await OpenRequestWithCredentialsAsync(vm, BearerAuth());
            var row = tab.HeadersEditor.AutomaticRows.Single(r => r.Key == "Authorization");

            await row.Sensitive.CopyCommand.ExecuteAsync(null);

            Assert.True(row.Sensitive.ShowMask);
            Assert.Equal(Jwt, await window.Clipboard!.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, ResponseViewModel Response) ShowSentRequest(Action<ResponseViewModel> show, string theme)
    {
        var response = new ResponseViewModel(App.Services.GetRequiredService<DialogService>(), App.Services.GetRequiredService<SettingsService>());
        response.SetResponse(new ResponseModel { StatusCode = 200, ReasonPhrase = "OK", ContentType = "text/plain", Body = "ok"u8.ToArray() });
        show(response);
        var view = new ResponseView { DataContext = response };
        var window = new Window
        {
            Content = view,
            Width = 900,
            Height = 560,
            RequestedThemeVariant = theme == "dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light,
        };
        window.Show();
        view.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 3;
        UiTestHelpers.Pump();
        return (window, response);
    }

    private static PreparedRequest SentRequest()
    {
        var request = new PreparedRequest
        {
            Method = "POST",
            Url = "https://example.test/secret?x=1",
            BodyMode = BodyMode.Raw,
            RawBody = "{\"hello\": \"world\"}",
        };
        request.Headers.Add(new KeyValueItem("Content-Type", "application/json"));
        request.Headers.Add(new KeyValueItem("Cookie", "session=abc123"));
        request.AddAutomaticHeader(new KeyValueItem("X-Custom-Token", "secret-value"), PreparedHeaderOrigin.Auth);
        request.AddAutomaticHeader(new KeyValueItem("Authorization", Jwt), PreparedHeaderOrigin.Auth);
        return request;
    }

    [AvaloniaFact]
    public void A_live_sent_request_has_masked_header_rows_with_real_values_behind_them()
    {
        var (window, response) = ShowSentRequest(r => r.SetSentRequest(SentRequest()), "dark");

        Assert.Equal("POST /secret?x=1 HTTP/1.1", response.SentRequestLine);
        Assert.Equal(["Host", "Content-Type", "Cookie", "X-Custom-Token", "Authorization"], response.SentHeaderRows.Select(row => row.Key));
        Assert.Equal([false, false, true, true, true], response.SentHeaderRows.Select(row => row.Sensitive.IsSensitive));
        Assert.Equal("{\"hello\": \"world\"}", response.SentBodyText);
        Assert.True(response.HasSentBody);
        Assert.Equal("secret-value", response.SentHeaderRows[3].Sensitive.Value);

        var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "ValueText").Select(text => text.Text).ToList();
        Assert.Equal(["example.test", "application/json", SensitiveHeaders.MaskText, SensitiveHeaders.MaskText, SensitiveHeaders.MaskText], shown);
        window.Close();
    }

    [AvaloniaFact]
    public void A_saved_sent_request_is_split_into_rows_that_stay_masked()
    {
        var saved = new SavedResponse
        {
            Response = new ResponseModel { StatusCode = 200, ReasonPhrase = "OK", ContentType = "text/plain", Body = "ok"u8.ToArray() },
            SentRequest = "GET /secret HTTP/1.1\nHost: example.test\nAuthorization: Bearer ••••••\nCookie: ••••••\n",
        };
        var (window, response) = ShowSentRequest(r => r.SetSaved([saved], show: true), "dark");

        Assert.Equal("GET /secret HTTP/1.1", response.SentRequestLine);
        Assert.Equal(["Host", "Authorization", "Cookie"], response.SentHeaderRows.Select(row => row.Key));
        Assert.Equal([false, true, true], response.SentHeaderRows.Select(row => row.Sensitive.IsSensitive));
        Assert.All(response.SentHeaderRows.Skip(1), row => Assert.Equal(SensitiveHeaders.MaskText, row.Sensitive.DisplayValue));
        Assert.False(response.HasSentBody);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Screenshot_of_the_sent_request(string theme)
    {
        var (window, response) = ShowSentRequest(r => r.SetSentRequest(SentRequest()), theme);
        UiTestHelpers.Screenshot(window, $"72-sensitive-sent-request-{theme}");

        response.SentHeaderRows[4].Sensitive.IsRevealed = true;
        UiTestHelpers.Screenshot(window, $"72-sensitive-sent-request-revealed-{theme}");
        window.Close();
    }

    private static (Window Window, KeyValueTable Table, KeyValueListViewModel List, List<string> Copied) ShowEditableTable(bool masksSensitiveKeys)
    {
        var copied = new List<string>();
        var list = new KeyValueListViewModel { MasksSensitiveKeys = masksSensitiveKeys, CopyText = text => { copied.Add(text); return Task.CompletedTask; } };
        list.Load([new("Authorization", Jwt), new("Accept", "application/json")]);
        var table = new KeyValueTable { DataContext = list };
        var window = new Window { Content = table, Width = 900, Height = 300 };
        window.Show();
        UiTestHelpers.Pump();
        return (window, table, list, copied);
    }

    private static List<(TextBlock Dots, VariableTextBox Box)> ValueCells(KeyValueTable table) =>
        table.GetVisualDescendants().OfType<Panel>().Where(panel => panel.Classes.Contains("valueCell"))
            .Select(panel => (panel.Children.OfType<TextBlock>().Single(), panel.Children.OfType<VariableTextBox>().Single())).ToList();

    [AvaloniaFact]
    public void An_authorization_row_in_the_editable_table_shows_dots_and_reveals_while_focused()
    {
        var (window, table, list, _) = ShowEditableTable(masksSensitiveKeys: true);
        var (authorization, accept) = (ValueCells(table)[0], ValueCells(table)[1]);

        UiTestHelpers.Screenshot(window, "72-sensitive-table-masked");
        Assert.True(authorization.Dots.IsVisible);
        Assert.Equal(SensitiveHeaders.MaskText, authorization.Dots.Text);
        Assert.Equal(0, authorization.Box.Opacity);
        Assert.False(accept.Dots.IsVisible);
        Assert.Equal(1, accept.Box.Opacity);

        authorization.Box.FocusEditor();
        UiTestHelpers.Pump();

        Assert.False(authorization.Dots.IsVisible);
        Assert.Equal(1, authorization.Box.Opacity);
        Assert.False(list.Rows[0].Sensitive.IsRevealed);

        accept.Box.FocusEditor();
        UiTestHelpers.Pump();

        Assert.True(authorization.Dots.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_eye_in_the_editable_table_keeps_the_value_shown_and_copy_copies_it_masked()
    {
        var (window, table, list, copied) = ShowEditableTable(masksSensitiveKeys: true);

        list.Rows[0].Sensitive.CopyCommand.Execute(null);
        Assert.Equal([Jwt], copied);

        list.Rows[0].Sensitive.ToggleRevealCommand.Execute(null);
        UiTestHelpers.Pump();

        Assert.False(ValueCells(table)[0].Dots.IsVisible);
        Assert.Equal(1, ValueCells(table)[0].Box.Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void Other_tables_do_not_mask_even_an_authorization_key()
    {
        var (window, table, list, _) = ShowEditableTable(masksSensitiveKeys: false);

        Assert.False(list.Rows[0].Sensitive.IsSensitive);
        Assert.All(ValueCells(table), cell => Assert.False(cell.Dots.IsVisible));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Screenshot_of_masked_revealed_and_expanded_rows(string theme)
    {
        var (window, list, _, token, _) = ShowList();
        window.RequestedThemeVariant = theme == "dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        UiTestHelpers.Pump();
        UiTestHelpers.Screenshot(window, $"72-sensitive-masked-{theme}");

        token.Sensitive.IsRevealed = true;
        UiTestHelpers.Screenshot(window, $"72-sensitive-revealed-{theme}");

        token.Sensitive.IsExpanded = true;
        UiTestHelpers.Pump();
        Assert.True(list.GetVisualDescendants().OfType<Popup>().First().IsOpen);
        UiTestHelpers.Screenshot(window, $"72-sensitive-flyout-{theme}");
        window.Close();
    }

    [AvaloniaFact]
    public void A_bearer_token_row_is_masked_on_one_line_with_ellipsis()
    {
        var (window, list, _, token, plain) = ShowList();

        var texts = ValueTexts(list).ToList();
        Assert.Equal(SensitiveHeaders.MaskText, texts[0].Text);
        Assert.Equal("application/json", texts[1].Text);
        Assert.All(texts, text =>
        {
            Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, text.TextWrapping);
            Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, text.TextTrimming);
        });
        Assert.False(plain.Sensitive.IsSensitive);
        Assert.True(token.Sensitive.IsSensitive);
        window.Close();
    }

    [AvaloniaFact]
    public void The_eye_reveals_only_its_own_row()
    {
        var (window, list, _, token, plain) = ShowList();

        ButtonNamed(list, "Reveal value").Command!.Execute(null);
        UiTestHelpers.Pump();

        Assert.True(token.Sensitive.IsRevealed);
        Assert.Equal(Jwt, ValueTexts(list).First().Text);
        Assert.Equal("Hide value", Avalonia.Automation.AutomationProperties.GetName(ButtonNamed(list, "Hide value")));
        Assert.False(plain.Sensitive.IsRevealed);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Copy_while_masked_copies_the_real_value_and_confirms()
    {
        var (window, list, copied, token, _) = ShowList();

        ButtonNamed(list, "Copy value").Command!.Execute(null);
        UiTestHelpers.Pump();

        Assert.Equal([Jwt], copied);
        Assert.True(token.Sensitive.ShowMask);
        Assert.True(token.Sensitive.IsCopied);
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Copied" && text.IsVisible);
        await UiTestHelpers.WaitUntil(() => !token.Sensitive.IsCopied);
        window.Close();
    }

    [AvaloniaFact]
    public void The_full_value_flyout_overlays_the_row_without_changing_its_height()
    {
        var (window, list, _, token, _) = ShowList();
        var rowHeightBefore = list.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "Row").Bounds.Height;

        ButtonNamed(list, "Show full value").Command!.Execute(null);
        UiTestHelpers.Pump();

        var popup = list.GetVisualDescendants().OfType<Popup>().First();
        Assert.True(popup.IsOpen);
        var popupTexts = popup.Child!.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(popupTexts, text => text.Text == $"{Jwt.Length} characters");
        Assert.Contains(popupTexts, text => text.Name == "FullValueText" && text.Text == SensitiveHeaders.MaskText);
        Assert.Equal(rowHeightBefore, list.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "Row").Bounds.Height);

        token.Sensitive.IsRevealed = true;
        UiTestHelpers.Pump();
        Assert.Contains(popup.Child!.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "FullValueText" && text.Text == Jwt);
        window.Close();
    }
}
