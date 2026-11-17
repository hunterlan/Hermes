using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

public class RequestUrlEditorViewModelTests
{
    private sealed class RaisedEvents
    {
        public int Changed { get; set; }
        public int ParamsEdited { get; set; }
        public List<HttpRequestModel> CurlPasted { get; } = [];
    }

    private static RaisedEvents Record(RequestUrlEditorViewModel editor)
    {
        var raised = new RaisedEvents();
        editor.Changed += () => raised.Changed++;
        editor.ParamsEdited += () => raised.ParamsEdited++;
        editor.CurlPasted += (_, e) => raised.CurlPasted.Add(e.Request);
        return raised;
    }

    private static List<string> Pairs(KeyValueListViewModel list) =>
        list.ToModels().Select(item => $"{(item.Enabled ? "" : "!")}{item.Key}={item.Value}").ToList();

    [AvaloniaFact]
    public void Typing_a_query_string_fills_the_params()
    {
        var editor = new RequestUrlEditorViewModel();

        editor.Url = "https://example.com/get?a=1&b=2";

        Assert.Equal(["a=1", "b=2"], Pairs(editor.Params));
    }

    [AvaloniaFact]
    public void A_url_edit_merges_into_the_params_and_keeps_a_disabled_row()
    {
        var editor = new RequestUrlEditorViewModel();
        editor.Url = "https://example.com/get?a=1&b=2";
        editor.Params.Rows[1].Enabled = false;

        editor.Url = "https://example.com/get?a=9&c=3&d=4";

        Assert.Equal(["a=9", "!b=2", "c=3", "d=4"], Pairs(editor.Params));
    }

    [AvaloniaFact]
    public void Editing_the_param_rows_rewrites_the_url()
    {
        var editor = new RequestUrlEditorViewModel();
        editor.Url = "https://example.com/get?a=1&b=2";

        editor.Params.Rows[1].Enabled = false;
        Assert.Equal("https://example.com/get?a=1", editor.Url);

        editor.Params.Rows[0].Value = "9";
        Assert.Equal("https://example.com/get?a=9", editor.Url);

        editor.Params.Rows[^1].Key = "c";
        editor.Params.Rows[2].Value = "3";
        Assert.Equal("https://example.com/get?a=9&c=3", editor.Url);

        editor.Params.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal("https://example.com/get?c=3", editor.Url);
        Assert.Equal(["!b=2", "c=3"], Pairs(editor.Params));
    }

    [AvaloniaFact]
    public void A_colon_segment_adds_a_path_variable_that_keeps_its_value_until_the_segment_is_removed()
    {
        var editor = new RequestUrlEditorViewModel();
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.Url = "https://example.com/users/:id";
        Assert.Equal(["id="], Pairs(editor.PathVariables));
        Assert.True(editor.HasPathVariables);
        Assert.Contains(nameof(RequestUrlEditorViewModel.HasPathVariables), changed);

        editor.PathVariables.Rows[0].Value = "42";
        editor.Url = "https://example.com/users/:id?x=1";
        Assert.Equal(["id=42"], Pairs(editor.PathVariables));

        editor.Url = "https://example.com/users";
        Assert.Empty(editor.PathVariables.ToModels());
        Assert.False(editor.HasPathVariables);
    }

    [AvaloniaFact]
    public void A_url_with_a_line_break_is_cleaned_once_the_dispatcher_runs()
    {
        var editor = new RequestUrlEditorViewModel();
        var raised = Record(editor);

        editor.Url = "  https://example.com/a\r\n/b?x=1\n ";

        Assert.Empty(editor.Params.ToModels());
        Assert.Equal(0, raised.Changed);

        UiTestHelpers.Pump();

        Assert.Equal("https://example.com/a/b?x=1", editor.Url);
        Assert.Equal(["x=1"], Pairs(editor.Params));
        Assert.Equal(1, raised.Changed);
    }

    [AvaloniaFact]
    public void A_pasted_curl_command_is_reported_once_the_dispatcher_runs_and_is_not_read_into_the_params()
    {
        var editor = new RequestUrlEditorViewModel();
        var raised = Record(editor);

        editor.Url = """curl -X POST https://example.com/post?x=1 -H 'X-Test: 1' -d '{"a":1}'""";

        Assert.Empty(raised.CurlPasted);
        UiTestHelpers.Pump();

        var request = Assert.Single(raised.CurlPasted);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://example.com/post?x=1", request.Url);
        Assert.Contains(request.Headers, header => header.Key == "X-Test" && header.Value == "1");
        Assert.Equal("""{"a":1}""", request.Body.Raw);
        Assert.Empty(editor.Params.ToModels());
        Assert.Empty(editor.PathVariables.ToModels());
        Assert.Equal(0, raised.Changed);
        Assert.Equal(0, raised.ParamsEdited);
    }

    [AvaloniaFact]
    public void Each_user_edit_raises_changed_exactly_once()
    {
        var editor = new RequestUrlEditorViewModel();
        var raised = Record(editor);

        editor.Url = "https://example.com/users/:id?a=1";
        Assert.Equal(1, raised.Changed);

        editor.Params.Rows[0].Value = "2";
        Assert.Equal(2, raised.Changed);

        editor.Params.Rows[^1].Key = "b";
        Assert.Equal(3, raised.Changed);

        editor.Params.Rows[0].RemoveCommand.Execute(null);
        Assert.Equal(4, raised.Changed);

        editor.PathVariables.Rows[0].Value = "7";
        Assert.Equal(5, raised.Changed);
    }

    [AvaloniaFact]
    public void Loading_raises_no_edit_events()
    {
        var editor = new RequestUrlEditorViewModel();
        var raised = Record(editor);

        editor.Load("https://example.com/users/:id?a=1", [new("a", "1"), new("b", "2", enabled: false)], [new("id", "7")]);
        UiTestHelpers.Pump();

        Assert.Equal("https://example.com/users/:id?a=1", editor.Url);
        Assert.Equal(["a=1", "!b=2"], Pairs(editor.Params));
        Assert.Equal(["id=7"], Pairs(editor.PathVariables));
        Assert.True(editor.HasPathVariables);
        Assert.Equal(0, raised.Changed);
        Assert.Equal(0, raised.ParamsEdited);
        Assert.Empty(raised.CurlPasted);
    }

    [AvaloniaFact]
    public void Loading_without_saved_params_reads_them_from_the_url()
    {
        var editor = new RequestUrlEditorViewModel();

        editor.Load("https://example.com/get?a=1&b=2", [], []);

        Assert.Equal(["a=1", "b=2"], Pairs(editor.Params));
        Assert.False(editor.HasPathVariables);
    }

    [AvaloniaFact]
    public void Setting_the_same_url_raises_nothing()
    {
        var editor = new RequestUrlEditorViewModel();
        editor.Load("https://example.com/get?a=1", [new("a", "1")], []);
        var raised = Record(editor);

        editor.Url = "https://example.com/get?a=1";

        Assert.Equal(0, raised.Changed);
        Assert.Equal(["a=1"], Pairs(editor.Params));
    }

    [AvaloniaFact]
    public void Params_edited_is_raised_only_for_edits_of_the_param_rows()
    {
        var editor = new RequestUrlEditorViewModel();
        var raised = Record(editor);

        editor.Load("https://example.com/users/:id?a=1", [], []);
        editor.Url = "https://example.com/users/:id?a=2";
        editor.PathVariables.Rows[0].Value = "7";
        Assert.Equal(0, raised.ParamsEdited);

        editor.Params.Rows[0].Value = "3";
        Assert.Equal(1, raised.ParamsEdited);
    }

    [AvaloniaTheory]
    [InlineData("{{baseUrl}}/get", true)]
    [InlineData("https://example.com/users/:id", true)]
    [InlineData("https://example.com/get?id={{$guid}}", true)]
    [InlineData("https://example.com/get?a=1", false)]
    [InlineData("", false)]
    public void The_url_has_substitutions_for_variables_and_path_segments(string url, bool hasSubstitutions)
    {
        var editor = new RequestUrlEditorViewModel();

        editor.Url = url;

        Assert.Equal(hasSubstitutions, editor.HasUrlSubstitutions);
    }
}
