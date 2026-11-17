using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class ComposerSectionsViewModelTests
{
    private sealed class Composer
    {
        public required KeyValueListViewModel QueryParams { get; init; }
        public required RequestBodyViewModel Body { get; init; }
        public required RequestSettingsViewModel Settings { get; init; }
        public required ComposerSectionsViewModel Sections { get; init; }
    }

    private static Composer CreateComposer()
    {
        var queryParams = new KeyValueListViewModel();
        var body = new RequestBodyViewModel(new DialogService());
        var settings = new RequestSettingsViewModel(App.Services.GetRequiredService<SettingsService>());
        return new Composer
        {
            QueryParams = queryParams,
            Body = body,
            Settings = settings,
            Sections = new ComposerSectionsViewModel(queryParams, body, settings),
        };
    }

    private static List<string?> RecordChanges(ComposerSectionsViewModel sections)
    {
        var changed = new List<string?>();
        sections.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    private static Option<BodyMode> Mode(BodyMode mode) => RequestBodyViewModel.BodyModes.Single(option => option.Value == mode);

    [AvaloniaFact]
    public void A_new_get_request_shows_none_of_the_optional_sections()
    {
        var sections = CreateComposer().Sections;

        Assert.False(sections.IsParamsSectionVisible);
        Assert.False(sections.IsBodySectionVisible);
        Assert.False(sections.IsSettingsSectionVisible);
        Assert.True(sections.HasUnusedSections);
        Assert.True(sections.HeadersExpanded);
        Assert.False(sections.AuthExpanded);
    }

    [AvaloniaFact]
    public void Params_show_while_one_is_active_and_summarise_how_many()
    {
        var composer = CreateComposer();
        var changed = RecordChanges(composer.Sections);

        composer.QueryParams.Load([new("a", "1"), new("b", "2"), new("c", "3", enabled: false)]);

        Assert.True(composer.Sections.IsParamsSectionVisible);
        Assert.Equal("2 active", composer.Sections.ParamsSummary);
        Assert.Contains(nameof(ComposerSectionsViewModel.IsParamsSectionVisible), changed);
        Assert.Contains(nameof(ComposerSectionsViewModel.ParamsSummary), changed);

        composer.QueryParams.Load([]);

        Assert.False(composer.Sections.IsParamsSectionVisible);
        Assert.Equal("", composer.Sections.ParamsSummary);
    }

    [AvaloniaFact]
    public void Path_variables_bring_the_params_section_on_screen()
    {
        var sections = CreateComposer().Sections;

        sections.HasPathVariables = true;
        Assert.True(sections.IsParamsSectionVisible);

        sections.HasPathVariables = false;
        Assert.False(sections.IsParamsSectionVisible);
    }

    [AvaloniaTheory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("GET", false)]
    [InlineData("DELETE", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    public void Only_methods_that_carry_a_body_bring_the_body_section_on_screen(string method, bool visible)
    {
        var sections = CreateComposer().Sections;

        sections.Method = method;

        Assert.Equal(visible, sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void A_body_with_content_shows_the_body_section()
    {
        var composer = CreateComposer();

        composer.Body.Load(new RequestBody { Mode = BodyMode.Raw, Raw = "x" });
        Assert.True(composer.Sections.IsBodySectionVisible);

        composer.Body.Load(new RequestBody());
        Assert.False(composer.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void An_unsupported_body_shows_the_body_section()
    {
        var composer = CreateComposer();

        composer.Body.Load(new RequestBody { UnsupportedJson = """{"mode":"graphql"}""" });

        Assert.True(composer.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void Settings_overrides_show_the_settings_section()
    {
        var composer = CreateComposer();

        composer.Settings.Load(new RequestSettings { TimeoutMs = 500 });
        Assert.True(composer.Sections.IsSettingsSectionVisible);

        composer.Settings.Load(new RequestSettings());
        Assert.False(composer.Sections.IsSettingsSectionVisible);
    }

    [AvaloniaFact]
    public void Unused_sections_remain_until_all_three_are_on_screen()
    {
        var sections = CreateComposer().Sections;
        var changed = RecordChanges(sections);

        sections.AddSectionCommand.Execute("params");
        sections.AddSectionCommand.Execute("body");
        Assert.True(sections.HasUnusedSections);
        sections.AddSectionCommand.Execute("settings");

        Assert.False(sections.HasUnusedSections);
        Assert.Contains(nameof(ComposerSectionsViewModel.HasUnusedSections), changed);
    }

    [AvaloniaTheory]
    [InlineData("params")]
    [InlineData("body")]
    [InlineData("settings")]
    public void A_chip_adds_its_section_expanded(string section)
    {
        var sections = CreateComposer().Sections;
        sections.ParamsExpanded = sections.BodyExpanded = sections.SettingsExpanded = false;

        sections.AddSectionCommand.Execute(section);

        Assert.Equal(section == "params", sections.IsParamsSectionVisible);
        Assert.Equal(section == "body", sections.IsBodySectionVisible);
        Assert.Equal(section == "settings", sections.IsSettingsSectionVisible);
        Assert.Equal(section == "params", sections.ParamsExpanded);
        Assert.Equal(section == "body", sections.BodyExpanded);
        Assert.Equal(section == "settings", sections.SettingsExpanded);
    }

    [AvaloniaFact]
    public void Changing_the_body_mode_keeps_the_section_after_the_body_is_emptied()
    {
        var composer = CreateComposer();

        composer.Body.SelectedBodyMode = Mode(BodyMode.Raw);
        composer.Body.SelectedBodyMode = Mode(BodyMode.None);

        Assert.True(composer.Sections.BodyAdded);
        Assert.True(composer.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void Editing_the_raw_text_does_not_keep_the_section()
    {
        var composer = CreateComposer();
        composer.Body.Load(new RequestBody { Mode = BodyMode.Raw, Raw = "x" });

        composer.Body.RawBody = "edited";
        composer.Body.Load(new RequestBody());

        Assert.False(composer.Sections.BodyAdded);
        Assert.False(composer.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void Loading_a_body_does_not_keep_the_section()
    {
        var composer = CreateComposer();

        composer.Body.Load(new RequestBody { Mode = BodyMode.Raw, Raw = "x" });
        composer.Body.Load(new RequestBody());

        Assert.False(composer.Sections.BodyAdded);
        Assert.False(composer.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void Editing_the_settings_keeps_the_section_after_they_are_reset()
    {
        var composer = CreateComposer();

        composer.Settings.TimeoutText = "500";
        composer.Settings.TimeoutText = "";

        Assert.True(composer.Sections.SettingsAdded);
        Assert.True(composer.Sections.IsSettingsSectionVisible);
    }

    [AvaloniaFact]
    public void A_pasted_curl_command_without_a_body_removes_the_body_section_after_a_raw_text_edit()
    {
        using var temp = new TempWorkspace();
        var model = new HttpRequestModel
        {
            Id = Guid.NewGuid(),
            Name = "Raw",
            Body = new RequestBody { Mode = BodyMode.Raw, Raw = "x" },
        };
        using var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), model);
        tab.Body.RawBody = "edited";
        Assert.True(tab.Sections.IsBodySectionVisible);

        tab.Url = "curl https://httpbin.org/get";
        UiTestHelpers.Pump();

        Assert.False(tab.Sections.BodyAdded);
        Assert.False(tab.Sections.IsBodySectionVisible);
    }

    [AvaloniaFact]
    public void Loading_settings_does_not_keep_the_section()
    {
        var composer = CreateComposer();

        composer.Settings.Load(new RequestSettings { TimeoutMs = 500 });
        composer.Settings.Load(new RequestSettings());

        Assert.False(composer.Sections.SettingsAdded);
        Assert.False(composer.Sections.IsSettingsSectionVisible);
    }

    [AvaloniaFact]
    public void Revealing_settings_adds_and_expands_the_section_and_asks_the_view_to_scroll()
    {
        var sections = CreateComposer().Sections;
        sections.SettingsExpanded = false;
        var requests = 0;
        sections.SettingsRevealRequested += () => requests++;

        sections.RevealSettings();

        Assert.True(sections.IsSettingsSectionVisible);
        Assert.True(sections.SettingsExpanded);
        Assert.Equal(1, requests);
    }

    [AvaloniaFact]
    public void Revealing_headers_expands_the_section_and_asks_the_view_to_scroll()
    {
        var sections = CreateComposer().Sections;
        sections.HeadersExpanded = false;
        var requests = 0;
        sections.HeadersRevealRequested += () => requests++;

        sections.RevealHeaders();

        Assert.True(sections.HeadersExpanded);
        Assert.Equal(1, requests);
    }

    [AvaloniaFact]
    public void Revealing_the_body_adds_and_expands_the_section_and_asks_the_view_to_scroll()
    {
        var sections = CreateComposer().Sections;
        sections.BodyExpanded = false;
        var requests = 0;
        sections.BodyRevealRequested += () => requests++;

        sections.RevealBody();

        Assert.True(sections.IsBodySectionVisible);
        Assert.True(sections.BodyExpanded);
        Assert.Equal(1, requests);
    }
}
