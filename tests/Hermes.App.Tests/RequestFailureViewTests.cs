using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Hermes.TestSupport;

namespace Hermes.App.Tests;

/// <summary>Requests that fail before a response show the four-part failure, and each next step works.</summary>
public class RequestFailureViewTests
{
    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static async Task<(MainWindow Window, RequestTabViewModel Tab)> SendAsync(string url, Action<RequestTabViewModel>? configure = null)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = url;
        configure?.Invoke(tab);
        await tab.SendCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();
        return (window, tab);
    }

    /// <summary>Presses a failure button the way the view does, with the action it was bound to.</summary>
    private static void Click(Control root, string buttonName)
    {
        var button = Find<Button>(root, buttonName);
        button.Command!.Execute(button.CommandParameter);
    }

    [AvaloniaFact]
    public async Task Refused_connection_shows_headline_facts_and_a_retry_that_sends_again()
    {
        var port = ClosedPort.Find();
        var (window, tab) = await SendAsync($"http://127.0.0.1:{port}/");

        Assert.Equal(RequestFailureKind.ConnectionRefused, tab.Response.Failure?.Model.Kind);
        Assert.Equal("Server isn't accepting connections", Find<SelectableTextBlock>(window, "FailureHeadlineText").Text);
        Assert.StartsWith("Nothing is listening on", Find<SelectableTextBlock>(window, "FailureFactsText").Text);
        Assert.True(Find<Button>(window, "RetryButton").IsEffectivelyVisible);
        Assert.False(Find<Button>(window, "EditUrlButton").IsEffectivelyVisible);

        var first = tab.Response.Failure;
        Click(window, "RetryButton");
        await UiTestHelpers.WaitUntil(() => !ReferenceEquals(tab.Response.Failure, first) && !tab.IsSending);

        Assert.Equal(RequestFailureKind.ConnectionRefused, tab.Response.Failure?.Model.Kind);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Invalid_url_offers_edit_url_which_asks_the_view_to_focus_the_url()
    {
        var (window, tab) = await SendAsync("http://");
        var focusRequests = 0;
        tab.UrlFocusRequested += () => focusRequests++;

        Assert.Equal(RequestFailureKind.InvalidUrl, tab.Response.Failure?.Model.Kind);
        Assert.True(Find<Button>(window, "EditUrlButton").IsEffectivelyVisible);
        Assert.Contains("Hermes stopped before sending anything.", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

        Click(window, "EditUrlButton");

        Assert.Equal(1, focusRequests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Invalid_header_offers_show_headers_which_expands_the_section()
    {
        var (window, tab) = await SendAsync("http://127.0.0.1:1/", t => t.HeadersEditor.Headers.Load([new KeyValueItem("Bad Name", "x")]));
        var revealRequests = 0;
        tab.HeadersRevealRequested += () => revealRequests++;
        tab.HeadersExpanded = false;

        Assert.Equal(RequestFailureKind.InvalidHeader, tab.Response.Failure?.Model.Kind);
        Click(window, "ShowHeadersButton");

        Assert.True(tab.HeadersExpanded);
        Assert.Equal(1, revealRequests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Missing_body_file_offers_choose_file_and_keeps_the_original_message_in_details()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hermes-missing-{Guid.NewGuid():N}.bin");
        var (window, tab) = await SendAsync("http://127.0.0.1:1/", t =>
        {
            t.SelectedMethod = "POST";
            t.SelectedBodyMode = RequestTabViewModel.BodyModes.Single(mode => mode.Value == BodyMode.Binary);
            t.BinaryFilePath = path;
        });

        Assert.Equal(RequestFailureKind.BodyFileUnreadable, tab.Response.Failure?.Model.Kind);
        Assert.Equal($"{path} doesn't exist.", Find<SelectableTextBlock>(window, "FailureFactsText").Text);
        Assert.True(Find<Button>(window, "ChooseFileButton").IsEffectivelyVisible);
        Assert.False(Find<Controls.CollapsibleSection>(window, "FailureDetailsSection").IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void Chosen_file_replaces_the_binary_path_that_failed()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.SelectedBodyMode = RequestTabViewModel.BodyModes.Single(mode => mode.Value == BodyMode.Binary);
        tab.BinaryFilePath = "/old/payload.bin";

        Assert.True(tab.UseChosenBodyFile("/new/payload.bin", "/old/payload.bin"));

        Assert.Equal("/new/payload.bin", tab.BinaryFilePath);
        window.Close();
    }

    [AvaloniaFact]
    public void Chosen_file_does_not_overwrite_a_binary_variable_and_shows_the_body_instead()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.SelectedBodyMode = RequestTabViewModel.BodyModes.Single(mode => mode.Value == BodyMode.Binary);
        tab.BinaryFilePath = "{{payloadFile}}";
        tab.BodyExpanded = false;
        var revealRequests = 0;
        tab.BodyRevealRequested += () => revealRequests++;

        Assert.False(tab.UseChosenBodyFile("/new/payload.bin", "/resolved/payload.bin"));

        Assert.Equal("{{payloadFile}}", tab.BinaryFilePath);
        Assert.True(tab.BodyExpanded);
        Assert.Equal(1, revealRequests);
        window.Close();
    }

    [AvaloniaFact]
    public void Chosen_file_replaces_only_the_form_data_rows_with_the_failed_path()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.SelectedBodyMode = RequestTabViewModel.BodyModes.Single(mode => mode.Value == BodyMode.FormData);
        tab.FormData.Load([
            new KeyValueItem("a", "/old/a.txt") { Kind = KeyValueKind.File },
            new KeyValueItem("b", "{{otherFile}}") { Kind = KeyValueKind.File },
        ]);

        Assert.True(tab.UseChosenBodyFile("/new/a.txt", "/old/a.txt"));

        var values = tab.FormData.Rows.Where(row => !row.IsPlaceholder).Select(row => row.Value).ToList();
        Assert.Equal(["/new/a.txt", "{{otherFile}}"], values);
        window.Close();
    }
}
