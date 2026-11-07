using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The Headers section of a request: own rows, rows inherited from folders and the collection, automatic ones.</summary>
public class InheritedHeadersTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    /// <summary>
    /// Collection "Shop" (X-Api-Version, X-Tenant) &gt; folder "Users" (X-Tenant, X-Trace) &gt; a request with its own
    /// X-Trace and X-Mine.
    /// </summary>
    private sealed record Setup(CollectionModel Collection, FolderModel Folder, HttpRequestModel Request);

    private static Setup CreateSetup(Action<Setup>? customize = null)
    {
        var collection = Workspace.CreateCollection("Shop " + Guid.NewGuid());
        collection.Headers = [new("X-Api-Version", "1"), new("X-Tenant", "shop")];
        var folder = new FolderModel { Name = "Users", Headers = [new("X-Tenant", "users"), new("X-Trace", "folder")] };
        Workspace.AddNode(collection, null, folder);
        var request = new HttpRequestModel
        {
            Name = "List",
            Url = "https://example.test/users",
            Headers = [new("X-Trace", "mine"), new("X-Mine", "yes")],
        };
        Workspace.AddNode(collection, folder, request);
        var setup = new Setup(collection, folder, request);
        customize?.Invoke(setup);
        Workspace.SaveCollection(collection);
        return setup;
    }

    private static async Task<RequestTabViewModel> OpenRequestAsync(MainWindowViewModel vm, HttpRequestModel request)
    {
        await vm.OpenRequestAsync(request);
        UiTestHelpers.Pump();
        return Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
    }

    private static List<string> ConfiguredHeaderNamesInSentOrder(RequestTabViewModel tab)
    {
        var context = Workspace.CreateContext(tab.RequestId);
        var prepared = RequestPreparer.Preview(tab.BuildModel(), context);
        return prepared.Headers.Where(h => prepared.OriginOf(h) == PreparedHeaderOrigin.Configured).Select(h => h.Key).ToList();
    }

    [AvaloniaFact]
    public async Task Inherited_rows_are_outermost_first_and_match_the_sent_order()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            Assert.Equal(["X-Api-Version", "X-Tenant", "X-Tenant", "X-Trace"], tab.InheritedHeaders.Select(h => h.Key));
            Assert.Equal([setup.Collection.Name, setup.Collection.Name, "Users", "Users"], tab.InheritedHeaders.Select(h => h.SourceName));

            var sentInherited = tab.InheritedHeaders.Where(h => h.IsSent && !h.IsOverridden).Select(h => h.Key);
            Assert.Equal(["X-Trace", "X-Mine", .. sentInherited], ConfiguredHeaderNamesInSentOrder(tab));
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Summary_counts_own_inherited_and_automatic_headers_and_leaves_out_zero_parts()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            // own: X-Trace, X-Mine. Sent inherited: X-Api-Version, X-Tenant (Users). Automatic: Host + 4 defaults.
            Assert.Equal("2 own · 2 inherited · 5 added automatically", tab.HeadersSummary);

            tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version").IsSent = false;
            Assert.Equal("2 own · 1 inherited · 5 added automatically", tab.HeadersSummary);

            var bare = await OpenRequestAsync(vm, new HttpRequestModel { Name = "Loose", Url = "https://example.test" });
            Assert.Equal("5 added automatically", bare.HeadersSummary);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Skipping_a_header_marks_the_tab_unsaved_changes_the_request_and_is_saved()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            Assert.False(tab.IsDirty);
            Assert.Contains("X-Api-Version", ConfiguredHeaderNamesInSentOrder(tab));

            tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version").IsSent = false;

            Assert.True(tab.IsDirty);
            Assert.DoesNotContain("X-Api-Version", ConfiguredHeaderNamesInSentOrder(tab));
            var skippedRow = tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version");
            Assert.True(skippedRow.IsSkipped);
            Assert.Equal("skipped here", skippedRow.StatusText);

            Assert.True(await tab.SaveAsync());
            Assert.False(tab.IsDirty);
            var saved = Assert.IsType<HttpRequestModel>(Workspace.Find(setup.Request.Id)!.Node);
            Assert.Equal(["X-Api-Version"], saved.SkippedInheritedHeaders);

            // Unskipping goes back to the saved state, so the tab is clean again.
            tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version").IsSent = true;
            Assert.True(tab.IsDirty);
            Assert.Contains("X-Api-Version", ConfiguredHeaderNamesInSentOrder(tab));
            Assert.Empty(tab.BuildModel().SkippedInheritedHeaders);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Toggling_a_checkbox_with_the_keyboard_keeps_the_focus_on_it()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            var rowsBefore = tab.InheritedHeaders.ToList();
            var checkBox = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.IsVisible && AutomationProperties.GetName(c) == "Send X-Api-Version header");
            Assert.True(checkBox.Focus());

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            UiTestHelpers.Pump();

            Assert.True(tab.IsDirty);
            Assert.True(tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version").IsSkipped);
            // The rows are the same instances, so the checkbox is still in the tree and focused.
            Assert.Equal(rowsBefore, tab.InheritedHeaders);
            Assert.True(checkBox.IsFocused);
            Assert.True(checkBox.IsAttachedToVisualTree());
            Assert.Equal("2 own · 1 inherited · 5 added automatically", tab.HeadersSummary);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Skipping_an_inherited_authorization_header_brings_the_auth_header_back()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s =>
        {
            s.Collection.Headers.Add(new("Authorization", "Token x"));
            s.Request.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "t" } };
        });
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            var row = tab.InheritedHeaders.Single(h => h.Key == "Authorization");
            Assert.DoesNotContain(tab.AutomaticHeaders, h => h.Key == "Authorization");
            Assert.True(row.ShowWarning);

            row.IsSent = false;
            Assert.Contains(tab.AutomaticHeaders, h => h.Key == "Authorization" && h.TagText == "from auth");
            Assert.False(row.ShowWarning);

            row.IsSent = true;
            Assert.DoesNotContain(tab.AutomaticHeaders, h => h.Key == "Authorization");
            Assert.True(row.ShowWarning);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task An_inherited_content_type_the_form_data_body_replaces_is_shown_as_not_sent()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s =>
        {
            s.Collection.Headers.Add(new("Content-Type", "text/plain"));
            s.Request.Body = new RequestBody { Mode = BodyMode.FormData };
        });
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            var row = tab.InheritedHeaders.Single(h => h.Key == "Content-Type");
            Assert.True(row.IsOverridden);
            Assert.Equal("replaced by body", row.StatusText);
            // Not counted: 2 own, 2 inherited (X-Api-Version, X-Tenant), Host + 4 defaults + multipart type + length.
            Assert.Equal("2 own · 2 inherited · 7 added automatically", tab.HeadersSummary);
            Assert.DoesNotContain("Content-Type", ConfiguredHeaderNamesInSentOrder(tab));
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_skipped_header_is_still_skipped_when_the_request_is_reopened()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s => s.Request.SkippedInheritedHeaders = ["x-api-version"]);
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            var row = tab.InheritedHeaders.Single(h => h.Key == "X-Api-Version");
            Assert.False(row.IsSent);
            Assert.True(row.IsSkipped);
            Assert.False(tab.IsDirty);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Headers_set_again_nearer_are_overridden_and_the_nearer_row_says_so()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            var overridden = tab.InheritedHeaders.Where(h => h.IsOverridden).ToList();
            Assert.Equal(["X-Tenant", "X-Trace"], overridden.Select(h => h.Key));
            // Shop's X-Tenant is won by the Users folder (below the table); Users' X-Trace by the request (above).
            Assert.Equal(["overridden by Users", "overridden above"], overridden.Select(h => h.StatusText));
            Assert.All(overridden, h => Assert.False(h.CanToggleSent));
            Assert.Equal(setup.Collection.Name, overridden[0].SourceName);

            // X-Trace is set by the request over the folder; X-Mine overrides nothing.
            var rows = tab.Headers.Rows.Where(r => !r.IsPlaceholder).ToList();
            Assert.Equal("Overrides Users", rows.Single(r => r.Key == "X-Trace").TagText);
            Assert.Equal("", rows.Single(r => r.Key == "X-Mine").TagText);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_header_named_like_the_auth_header_gets_a_warning()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s =>
        {
            s.Request.Headers.Add(new("authorization", "Basic abc"));
            s.Request.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "t" } };
        });
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            var rows = tab.Headers.Rows.Where(r => !r.IsPlaceholder).ToList();
            Assert.Equal("Replaces auth header", rows.Single(r => r.Key == "authorization").WarningTagText);
            Assert.All(rows.Where(r => r.Key != "authorization"), r => Assert.Equal("", r.WarningTagText));
            Assert.DoesNotContain(tab.AutomaticHeaders, h => h.TagText == "from auth");

            // An inherited header that replaces the auth header is warned about on its own row.
            var inheritedSetup = CreateSetup(s => s.Collection.Headers.Add(new("Authorization", "Token x")));
            try
            {
                inheritedSetup.Request.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "t" } };
                Workspace.SaveCollection(inheritedSetup.Collection);
                var inheritedTab = await OpenRequestAsync(vm, inheritedSetup.Request);
                var row = inheritedTab.InheritedHeaders.Single(h => h.Key == "Authorization");
                Assert.True(row.IsTagWarning);
                Assert.Equal("Replaces auth header", row.StatusText);
            }
            finally
            {
                Workspace.DeleteCollection(inheritedSetup.Collection);
            }
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_source_chip_opens_the_folder_or_collection_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            tab.InheritedHeaders.First(h => h.SourceName == "Users").OpenSourceTabCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => vm.ActiveTab is FolderTabViewModel);
            Assert.Equal(setup.Folder.Id, vm.ActiveTab!.EntityId);

            tab.InheritedHeaders.First(h => h.IsSourceCollection).OpenSourceTabCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => vm.ActiveTab is CollectionTabViewModel);
            Assert.Equal(setup.Collection.Id, vm.ActiveTab!.EntityId);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Editing_a_collection_refreshes_the_inherited_rows()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup();
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            Assert.DoesNotContain(tab.InheritedHeaders, h => h.Key == "X-New");

            setup.Collection.Headers.Add(new("X-New", "1"));
            Workspace.SaveCollection(setup.Collection);

            Assert.Contains(tab.InheritedHeaders, h => h.Key == "X-New");
            Assert.False(tab.IsDirty);
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Automatic_headers_say_whether_they_come_from_the_body_or_auth()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s =>
        {
            s.Collection.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "t" } };
            s.Request.Auth = AuthConfig.Inherit();
            s.Request.Body = new RequestBody { Mode = BodyMode.Raw, Raw = "{}", RawLanguage = RawLanguage.Json };
        });
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);

            string TagOf(string key) => tab.AutomaticHeaders.Single(h => h.Key == key).TagText;
            Assert.Equal($"from auth · {setup.Collection.Name}", TagOf("Authorization"));
            Assert.Equal("from body", TagOf("Content-Type"));
            Assert.Equal("from body", TagOf("Content-Length"));
            Assert.Equal("added automatically", TagOf("Host"));
            Assert.Equal("added automatically", TagOf("User-Agent"));
            // Inherited and own headers never show up as automatic.
            Assert.DoesNotContain(tab.AutomaticHeaders, h => h.Key is "X-Api-Version" or "X-Mine");

            tab.Auth.SelectedType = tab.Auth.Types.Single(t => t.Value == AuthType.Bearer);
            tab.Auth.BearerToken = "own";
            Assert.Equal("from auth", TagOf("Authorization"));
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_content_type_set_by_the_user_is_not_marked_as_from_the_body()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var setup = CreateSetup(s =>
        {
            s.Request.Headers.Add(new("Content-Type", "text/plain"));
            s.Request.Body = new RequestBody { Mode = BodyMode.Raw, Raw = "x", RawLanguage = RawLanguage.Json };
        });
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            Assert.DoesNotContain(tab.AutomaticHeaders, h => h.Key == "Content-Type");
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Header_list_shows_the_source_chip_the_skip_checkbox_and_the_overridden_state(string themeName)
    {
        var opened = new List<string>();
        var toggled = new List<(string Key, bool IsSent)>();
        List<HeaderRowViewModel> rows =
        [
            new("User-Agent", "Hermes", "added automatically"),
            new("X-Api-Version", "1", "")
            {
                SourceName = "Shop", IsSourceCollection = true, OpenSource = () => opened.Add("Shop"), CanSkip = true,
                SentChanged = (row, isSent) => toggled.Add((row.Key, isSent)),
            },
            new("X-Skipped", "2", "") { SourceName = "Users", CanSkip = true, InitiallySent = false },
            new("X-Tenant", "shop", "") { SourceName = "Shop", CanSkip = true, IsOverridden = true },
        ];
        var view = new HeaderListView { Rows = rows, Heading = "Inherited", Hint = "untick to skip one" };
        var window = new Window
        {
            Content = view,
            Width = 700,
            Height = 260,
            RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        window.Show();
        UiTestHelpers.Pump();

        var checkBoxes = view.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsVisible).ToList();
        Assert.Equal(3, checkBoxes.Count);
        Assert.Equal([true, false, true], checkBoxes.Select(c => c.IsChecked == true));
        Assert.False(checkBoxes[2].IsEnabled);

        var chips = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible && b is not ToggleButton).ToList();
        Assert.Equal(3, chips.Count);
        chips[0].Command!.Execute(null);
        Assert.Equal(["Shop"], opened);

        checkBoxes[0].IsChecked = false;
        Assert.Equal([("X-Api-Version", false)], toggled);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
        Assert.Contains("added automatically", texts);
        Assert.Contains("skipped here", texts);
        Assert.Contains("overridden above", texts);
        Assert.Contains("Inherited", texts);
        Assert.Contains("untick to skip one", texts);

        // Only the overridden row's key and value are crossed out.
        TextBlock KeyOf(string key) => view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == key);
        Assert.Same(TextDecorations.Strikethrough, KeyOf("X-Tenant").TextDecorations);
        Assert.Null(KeyOf("X-Api-Version").TextDecorations);
        Assert.Null(KeyOf("User-Agent").TextDecorations);

        var plainRowCheckBoxCount = view.GetVisualDescendants().OfType<CheckBox>().Count(c => !c.IsVisible);
        Assert.Equal(1, plainRowCheckBoxCount);

        UiTestHelpers.Screenshot(window, "63-header-list-" + themeName.ToLowerInvariant());
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task Headers_section_in_both_themes(string themeName)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        window.RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        // Tall enough for the whole Headers section above the response pane.
        window.Height = 1500;
        var setup = CreateSetup(s => s.Request.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "t" } });
        setup.Request.Headers.Add(new("Authorization", "Basic x"));
        Workspace.SaveCollection(setup.Collection);
        try
        {
            var tab = await OpenRequestAsync(vm, setup.Request);
            tab.HeadersExpanded = true;
            UiTestHelpers.Pump();
            var lists = window.GetVisualDescendants().OfType<HeaderListView>()
                .Where(list => ReferenceEquals(list.Rows, tab.InheritedHeaders) || ReferenceEquals(list.Rows, tab.AutomaticHeaders))
                .ToList();
            Assert.Equal(2, lists.Count);
            UiTestHelpers.Screenshot(window, "63-request-headers-" + themeName.ToLowerInvariant());
        }
        finally
        {
            Workspace.DeleteCollection(setup.Collection);
            window.Close();
        }
    }
}
