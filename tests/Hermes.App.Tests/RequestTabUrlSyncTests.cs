using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>
/// The URL, the query param rows and the path variable rows edit each other. These pin how they stay in step, and
/// the two edits that are deferred to the dispatcher (a pasted cURL command and a URL with a line break).
/// </summary>
public class RequestTabUrlSyncTests
{
    private static RequestTabViewModel OpenTab(TempWorkspace temp, HttpRequestModel? model = null) =>
        new(RequestTabTestServices.Create(temp), model ?? new HttpRequestModel { Id = Guid.NewGuid(), Name = "Probe" });

    private static List<string> Pairs(KeyValueListViewModel list) =>
        list.ToModels().Select(item => $"{(item.Enabled ? "" : "!")}{item.Key}={item.Value}").ToList();

    [AvaloniaFact]
    public void Typing_a_query_string_fills_the_params()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);

        tab.UrlEditor.Url = "https://example.com/get?a=1&b=2";

        Assert.Equal(["a=1", "b=2"], Pairs(tab.UrlEditor.Params));
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public void Editing_a_param_row_rewrites_the_url()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        tab.UrlEditor.Url = "https://example.com/get?a=1&b=2";

        tab.UrlEditor.Params.Rows[1].Value = "3";
        Assert.Equal("https://example.com/get?a=1&b=3", tab.UrlEditor.Url);

        tab.UrlEditor.Params.Rows[0].Key = "first";
        Assert.Equal("https://example.com/get?first=1&b=3", tab.UrlEditor.Url);
    }

    [AvaloniaFact]
    public void Adding_a_param_on_the_blank_row_appends_it_to_the_url()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        tab.UrlEditor.Url = "https://example.com/get?a=1";

        tab.UrlEditor.Params.Rows[^1].Key = "c";
        Assert.Equal("https://example.com/get?a=1&c", tab.UrlEditor.Url);

        tab.UrlEditor.Params.Rows[1].Value = "9";
        Assert.Equal("https://example.com/get?a=1&c=9", tab.UrlEditor.Url);
    }

    [AvaloniaFact]
    public void Disabling_a_param_row_drops_it_from_the_url_but_keeps_the_row()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        tab.UrlEditor.Url = "https://example.com/get?a=1&b=2";

        tab.UrlEditor.Params.Rows[0].Enabled = false;

        Assert.Equal("https://example.com/get?b=2", tab.UrlEditor.Url);
        Assert.Equal(["!a=1", "b=2"], Pairs(tab.UrlEditor.Params));
    }

    [AvaloniaFact]
    public void A_disabled_param_survives_a_later_url_edit()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        tab.UrlEditor.Url = "https://example.com/get?a=1&b=2";
        tab.UrlEditor.Params.Rows[0].Enabled = false;

        tab.UrlEditor.Url = "https://example.com/get?b=5&c=6";

        Assert.Equal(["!a=1", "b=5", "c=6"], Pairs(tab.UrlEditor.Params));
    }

    [AvaloniaFact]
    public void Removing_a_param_row_rewrites_the_url()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        tab.UrlEditor.Url = "https://example.com/get?a=1&b=2";

        tab.UrlEditor.Params.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal("https://example.com/get?b=2", tab.UrlEditor.Url);

        tab.UrlEditor.Params.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal("https://example.com/get", tab.UrlEditor.Url);
        Assert.Empty(tab.UrlEditor.Params.ToModels());
    }

    [AvaloniaFact]
    public void A_path_variable_appears_for_a_colon_segment_and_keeps_its_value_when_the_url_changes()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);

        tab.UrlEditor.Url = "https://example.com/users/:id";
        Assert.Equal(["id="], Pairs(tab.UrlEditor.PathVariables));
        Assert.True(tab.UrlEditor.HasPathVariables);

        tab.UrlEditor.PathVariables.Rows[0].Value = "42";
        tab.UrlEditor.Url = "https://example.com/users/:id?x=1";
        Assert.Equal(["id=42"], Pairs(tab.UrlEditor.PathVariables));

        tab.UrlEditor.Url = "https://example.com/v2/users/:id/posts/:postId";
        Assert.Equal(["id=42", "postId="], Pairs(tab.UrlEditor.PathVariables));

        tab.UrlEditor.Url = "https://example.com/v2/users";
        Assert.Empty(tab.UrlEditor.PathVariables.ToModels());
        Assert.False(tab.UrlEditor.HasPathVariables);
    }

    [AvaloniaFact]
    public void A_pasted_curl_command_is_imported_and_keeps_the_tab_identity()
    {
        using var temp = new TempWorkspace();
        var model = new HttpRequestModel { Id = Guid.NewGuid(), Name = "Keep me" };
        using var tab = OpenTab(temp, model);

        tab.UrlEditor.Url = """curl -X POST https://example.com/post?x=1 -H 'X-Test: 1' -d '{"a":1}'""";
        UiTestHelpers.Pump();

        var built = tab.BuildModel();
        Assert.Equal("POST", built.Method);
        Assert.Equal("https://example.com/post?x=1", tab.UrlEditor.Url);
        Assert.Equal(["x=1"], Pairs(tab.UrlEditor.Params));
        Assert.Contains(built.Headers, header => header.Key == "X-Test" && header.Value == "1");
        Assert.Equal(BodyMode.Raw, built.Body.Mode);
        Assert.Equal("""{"a":1}""", built.Body.Raw);
        Assert.Equal(model.Id, tab.RequestId);
        Assert.Equal(model.Id, built.Id);
        Assert.Equal("Keep me", tab.Name);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public void A_url_with_a_line_break_is_cleaned_after_the_dispatcher_runs()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);

        tab.UrlEditor.Url = "  https://example.com/a\r\n/b?x=1\n ";
        UiTestHelpers.Pump();

        Assert.Equal("https://example.com/a/b?x=1", tab.UrlEditor.Url);
        Assert.Equal(["x=1"], Pairs(tab.UrlEditor.Params));
    }

    [AvaloniaFact]
    public void Setting_the_same_url_on_a_clean_tab_keeps_it_clean()
    {
        using var temp = new TempWorkspace();
        var model = new HttpRequestModel
        {
            Id = Guid.NewGuid(),
            Name = "Clean",
            Url = "https://example.com/users/:id?x=1",
            QueryParams = [new("x", "1")],
            PathVariables = [new("id", "7")],
        };
        using var tab = OpenTab(temp, model);

        tab.UrlEditor.Url = "https://example.com/users/:id?x=1";
        Assert.False(tab.IsDirty);
        Assert.Equal(["id=7"], Pairs(tab.UrlEditor.PathVariables));

        tab.UrlEditor.Url = "https://example.com/users/:id?x=2";
        Assert.True(tab.IsDirty);

        tab.UrlEditor.Url = "https://example.com/users/:id?x=1";
        Assert.False(tab.IsDirty);
        Assert.Equal(["x=1"], Pairs(tab.UrlEditor.Params));
    }
}
