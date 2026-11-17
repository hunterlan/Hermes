using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.Tests;

public class RequestBodyViewModelTests
{
    private static RequestBodyViewModel CreateBody() => new(new DialogService());

    private static Option<BodyMode> Mode(BodyMode mode) => RequestBodyViewModel.BodyModes.Single(option => option.Value == mode);

    private static Option<RawLanguage> Language(RawLanguage language) =>
        RequestBodyViewModel.RawLanguages.Single(option => option.Value == language);

    private static string Serialized(RequestBody body) =>
        ModelCloner.Serialize(new HttpRequestModel { Id = Guid.Empty, Body = body });

    public static TheoryData<string, RequestBody> RoundTripBodies() => new()
    {
        { "none", new RequestBody() },
        { "json", new RequestBody { Mode = BodyMode.Raw, RawLanguage = RawLanguage.Json, Raw = """{"a":1}""" } },
        { "text", new RequestBody { Mode = BodyMode.Raw, RawLanguage = RawLanguage.Text, Raw = "hello" } },
        { "xml", new RequestBody { Mode = BodyMode.Raw, RawLanguage = RawLanguage.Xml, Raw = "<a/>" } },
        { "html", new RequestBody { Mode = BodyMode.Raw, RawLanguage = RawLanguage.Html, Raw = "<p>hi</p>" } },
        { "javascript", new RequestBody { Mode = BodyMode.Raw, RawLanguage = RawLanguage.JavaScript, Raw = "const a = 1;" } },
        {
            "form-data", new RequestBody
            {
                Mode = BodyMode.FormData,
                FormData = [new("name", "Ada"), new("avatar", "/tmp/a.png", enabled: false) { Kind = KeyValueKind.File }],
            }
        },
        { "url-encoded", new RequestBody { Mode = BodyMode.UrlEncoded, UrlEncoded = [new("user", "a b"), new("pw", "&=", enabled: false)] } },
        { "binary", new RequestBody { Mode = BodyMode.Binary, BinaryFilePath = "/tmp/data.bin" } },
        { "unsupported", new RequestBody { UnsupportedJson = """{"mode":"graphql"}""" } },
    };

    [AvaloniaTheory]
    [MemberData(nameof(RoundTripBodies))]
    public void Loading_a_body_and_reading_it_back_gives_the_same_body(string name, RequestBody body)
    {
        var vm = CreateBody();

        vm.Load(body);

        Assert.Equal(Serialized(body), Serialized(vm.ToModel()));
        Assert.NotNull(name);
    }

    [AvaloniaFact]
    public void Mode_flags_follow_the_selected_mode()
    {
        var vm = CreateBody();

        Assert.True(vm.IsBodyNone);
        Assert.False(vm.HasBody);

        vm.SetBodyModeCommand.Execute(BodyMode.FormData);
        Assert.True(vm.IsBodyFormData);
        Assert.True(vm.HasBody);
        Assert.False(vm.IsBodyNone);

        vm.SetBodyModeCommand.Execute(BodyMode.UrlEncoded);
        Assert.True(vm.IsBodyUrlEncoded);

        vm.SetBodyModeCommand.Execute(BodyMode.Raw);
        Assert.True(vm.IsBodyRaw);

        vm.SetBodyModeCommand.Execute(BodyMode.Binary);
        Assert.True(vm.IsBodyBinary);
        Assert.False(vm.IsBodyRaw);
    }

    [AvaloniaFact]
    public void Flag_changes_are_announced_when_the_mode_changes()
    {
        var vm = CreateBody();
        var announced = new List<string?>();
        vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        vm.SelectedBodyMode = Mode(BodyMode.Raw);

        Assert.Contains(nameof(RequestBodyViewModel.IsBodyRaw), announced);
        Assert.Contains(nameof(RequestBodyViewModel.IsBodyNone), announced);
        Assert.Contains(nameof(RequestBodyViewModel.HasBody), announced);
        Assert.Contains(nameof(RequestBodyViewModel.Summary), announced);
    }

    [AvaloniaFact]
    public void Summary_is_the_mode_or_for_raw_the_language()
    {
        var vm = CreateBody();
        Assert.Equal("none", vm.Summary);

        vm.SelectedBodyMode = Mode(BodyMode.FormData);
        Assert.Equal("form-data", vm.Summary);

        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        vm.SelectedRawLanguage = Language(RawLanguage.Xml);
        Assert.Equal("XML", vm.Summary);
        Assert.Equal("xml", vm.RawEditorLanguage);
    }

    [AvaloniaFact]
    public void Binary_label_names_the_file_or_says_none_is_selected()
    {
        var vm = CreateBody();
        Assert.Equal("No file selected", vm.BinaryFileLabel);

        vm.BinaryFilePath = "/tmp/data.bin";

        Assert.Equal("data.bin", vm.BinaryFileLabel);
    }

    [AvaloniaFact]
    public void Unsupported_body_is_flagged_only_while_the_mode_is_none_and_is_kept()
    {
        var vm = CreateBody();
        vm.Load(new RequestBody { UnsupportedJson = """{"mode":"graphql"}""" });

        Assert.True(vm.HasUnsupportedBody);

        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        Assert.False(vm.HasUnsupportedBody);
        Assert.Equal("""{"mode":"graphql"}""", vm.ToModel().UnsupportedJson);
    }

    [AvaloniaFact]
    public void Beautify_formats_json()
    {
        var vm = CreateBody();
        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        vm.SelectedRawLanguage = Language(RawLanguage.Json);
        vm.RawBody = """{"a":1,"b":[1,2]}""";

        vm.BeautifyCommand.Execute(null);

        Assert.Contains('\n', vm.RawBody);
        Assert.Contains("\"a\": 1", vm.RawBody);
    }

    [AvaloniaFact]
    public void Beautify_formats_xml()
    {
        var vm = CreateBody();
        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        vm.SelectedRawLanguage = Language(RawLanguage.Xml);
        vm.RawBody = "<a><b>1</b></a>";

        vm.BeautifyCommand.Execute(null);

        Assert.Contains('\n', vm.RawBody);
    }

    [AvaloniaFact]
    public void Beautify_leaves_text_alone()
    {
        var vm = CreateBody();
        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        vm.SelectedRawLanguage = Language(RawLanguage.Text);
        vm.RawBody = "{\"a\":1}";

        vm.BeautifyCommand.Execute(null);

        Assert.Equal("{\"a\":1}", vm.RawBody);
    }

    [AvaloniaFact]
    public void Chosen_file_replaces_the_binary_path_that_failed()
    {
        var vm = CreateBody();
        vm.Load(new RequestBody { Mode = BodyMode.Binary, BinaryFilePath = "/old/payload.bin" });

        Assert.True(vm.UseChosenBodyFile("/new/payload.bin", "/old/payload.bin"));

        Assert.Equal("/new/payload.bin", vm.BinaryFilePath);
    }

    [AvaloniaFact]
    public void Chosen_file_does_not_overwrite_a_binary_variable()
    {
        var vm = CreateBody();
        vm.Load(new RequestBody { Mode = BodyMode.Binary, BinaryFilePath = "{{payloadFile}}" });

        Assert.False(vm.UseChosenBodyFile("/new/payload.bin", "/resolved/payload.bin"));

        Assert.Equal("{{payloadFile}}", vm.BinaryFilePath);
    }

    [AvaloniaFact]
    public void Chosen_file_replaces_only_the_form_data_rows_with_the_failed_path()
    {
        var vm = CreateBody();
        vm.Load(new RequestBody
        {
            Mode = BodyMode.FormData,
            FormData =
            [
                new("a", "/old/a.txt") { Kind = KeyValueKind.File },
                new("b", "{{otherFile}}") { Kind = KeyValueKind.File },
            ],
        });

        Assert.True(vm.UseChosenBodyFile("/new/a.txt", "/old/a.txt"));

        var values = vm.FormData.Rows.Where(row => !row.IsPlaceholder).Select(row => row.Value).ToList();
        Assert.Equal(["/new/a.txt", "{{otherFile}}"], values);
    }

    [AvaloniaFact]
    public void Edits_raise_changed()
    {
        var vm = CreateBody();
        var changes = 0;
        vm.Changed += () => changes++;

        vm.SelectedBodyMode = Mode(BodyMode.Raw);
        vm.SelectedRawLanguage = Language(RawLanguage.Xml);
        vm.RawBody = "<a/>";
        vm.BinaryFilePath = "/tmp/x";
        vm.FormData.Load([]);
        vm.FormData.Rows[0].Key = "k";
        vm.UrlEncoded.Rows[0].Key = "k";

        Assert.True(changes >= 6, $"Changed was raised {changes} times");
    }

    [AvaloniaFact]
    public void Loading_does_not_raise_changed()
    {
        var vm = CreateBody();
        var changes = 0;
        vm.Changed += () => changes++;

        vm.Load(new RequestBody
        {
            Mode = BodyMode.FormData,
            Raw = "x",
            RawLanguage = RawLanguage.Xml,
            FormData = [new("a", "b")],
            UrlEncoded = [new("c", "d")],
            BinaryFilePath = "/tmp/x",
        });

        Assert.Equal(0, changes);
    }
}
