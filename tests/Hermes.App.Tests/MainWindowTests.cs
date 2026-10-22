using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Main_window_opens_with_a_request_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();

        Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        UiTestHelpers.Screenshot(window, "01-empty-request");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Sending_a_request_shows_the_response()
    {
        using var server = new JsonServer();
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        tab.Url = server.Url + "users/:id?page=2&limit=10";
        UiTestHelpers.Pump();
        Assert.Equal(["page", "limit"], tab.Params.ToModels().Select(p => p.Key));
        Assert.Equal("id", Assert.Single(tab.PathVariables.Rows).Key);
        tab.PathVariables.Rows[0].Value = "42";

        await tab.SendCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.True(tab.Response.HasResponse, tab.Response.Error);
        Assert.Equal("200 OK", tab.Response.StatusText);
        Assert.Equal("json", tab.Response.Language);
        Assert.Contains("\"path\": \"/users/42?page=2&limit=10\"", tab.Response.PrettyText);
        Assert.Contains(tab.Response.Cookies, c => c.Name == "session");
        Assert.True(tab.IsDirty);
        UiTestHelpers.Screenshot(window, "02-response");
        window.Close();
    }

    [AvaloniaFact]
    public void Editing_params_table_updates_url()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.NewRequestCommand.Execute(null);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        tab.Url = "https://api.example.com/items";
        var placeholder = tab.Params.Rows.Last();
        placeholder.Key = "q";
        placeholder.Value = "hermes";
        Assert.Equal("https://api.example.com/items?q=hermes", tab.Url);

        tab.Params.Rows[0].Enabled = false;
        Assert.Equal("https://api.example.com/items", tab.Url);
        Assert.Single(tab.Params.ToModels());
        window.Close();
    }

    [AvaloniaFact]
    public void Pasting_curl_into_url_imports_request()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.NewRequestCommand.Execute(null);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        tab.Url = "curl -X POST https://api.example.com/users -H 'Content-Type: application/json' -d '{\"name\":\"x\"}'";
        UiTestHelpers.Pump();

        Assert.Equal("POST", tab.SelectedMethod);
        Assert.Equal("https://api.example.com/users", tab.Url);
        Assert.True(tab.IsBodyRaw);
        Assert.Equal("{\"name\":\"x\"}", tab.RawBody);
        UiTestHelpers.Screenshot(window, "03-curl-import");
        window.Close();
    }
}
