using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Hermes.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>A request that fails its certificate check shows the four-part failure, and its next steps work.</summary>
public class CertificateFailureViewTests
{
    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static async Task<(MainWindow Window, RequestTabViewModel Tab)> SendToSelfSignedServerAsync(LocalHttpsServer server)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.UrlEditor.Url = server.Url;
        await tab.Sending.SendCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();
        return (window, tab);
    }

    [AvaloniaFact]
    public async Task Failed_certificate_check_shows_headline_facts_next_step_and_collapsed_details()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        var (window, tab) = await SendToSelfSignedServerAsync(server);

        Assert.False(tab.Response.HasResponse);
        Assert.Equal(RequestFailureKind.CertificateSelfSigned, tab.Response.Failure?.Model.Kind);
        Assert.Equal("Certificate is self-signed", Find<SelectableTextBlock>(window, "FailureHeadlineText").Text);
        Assert.StartsWith("Signed by the server itself", Find<SelectableTextBlock>(window, "FailureFactsText").Text);
        Assert.True(Find<Button>(window, "TurnOffVerificationButton").IsEffectivelyVisible);
        Assert.True(Find<Button>(window, "OpenRequestSettingsButton").IsEffectivelyVisible);
        Assert.Contains("Only for servers you trust", Find<TextBlock>(window, "FailureActionCaptionText").Text);

        var details = Find<CollapsibleSection>(window, "FailureDetailsSection");
        Assert.False(details.IsExpanded);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "CopyFailureDetailsButton" && button.IsEffectivelyVisible);

        details.IsExpanded = true;
        UiTestHelpers.Pump();
        Assert.True(Find<Button>(window, "CopyFailureDetailsButton").IsEffectivelyVisible);
        Assert.False(Find<SelectableTextBlock>(window, "FailureOriginalMessageText").IsEffectivelyVisible);
        var visibleLabels = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(label => label.IsEffectivelyVisible).Select(label => label.Text).ToList();
        Assert.Contains("Issued to", visibleLabels);
        Assert.Contains("Issued by", visibleLabels);
        Assert.Contains("Check failed", visibleLabels);
        UiTestHelpers.Screenshot(window, "certificate-failure");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Copy_details_sits_in_the_details_header_above_the_rows_and_stays_in_view()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        var (window, tab) = await SendToSelfSignedServerAsync(server);
        var details = Find<CollapsibleSection>(window, "FailureDetailsSection");
        details.IsExpanded = true;
        UiTestHelpers.Pump();

        var copyButton = Find<Button>(window, "CopyFailureDetailsButton");
        // The first row of Details is the "Server" line, which the view shows above the certificate's own rows.
        var firstDetailLabelText = tab.Response.Failure!.Details[0].Label;
        Assert.Equal("Server", firstDetailLabelText);
        var firstDetailLabel = window.GetVisualDescendants().OfType<TextBlock>()
            .First(label => label.IsEffectivelyVisible && label.Text == firstDetailLabelText);
        var scroller = copyButton.FindAncestorOfType<ScrollViewer>()!;
        var copyTop = copyButton.TranslatePoint(default, scroller)!.Value.Y;
        var detailTop = firstDetailLabel.TranslatePoint(default, scroller)!.Value.Y;

        // Above the rows, so a long list of details can't push it below the edge of the pane.
        Assert.True(copyTop < detailTop, $"Copy details at {copyTop} should be above the first row at {detailTop}.");
        Assert.True(copyTop >= 0 && copyTop + copyButton.Bounds.Height <= scroller.Viewport.Height,
            "Copy details should be inside the visible part of the failure block without scrolling.");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Turning_verification_off_sets_the_request_override_and_the_resent_request_succeeds()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        var appWideVerifySsl = App.Services.GetRequiredService<SettingsService>().Current.VerifySsl;
        var (window, tab) = await SendToSelfSignedServerAsync(server);
        Assert.Null(tab.Settings.VerifySslOverride);

        var turnOffButton = Find<Button>(window, "TurnOffVerificationButton");
        turnOffButton.Command!.Execute(turnOffButton.CommandParameter);
        await UiTestHelpers.WaitUntil(() => tab.Response.HasResponse);

        Assert.False(tab.Settings.VerifySslOverride);
        Assert.Null(tab.Response.Failure);
        Assert.Equal("200 OK", tab.Response.StatusText);
        Assert.Equal(LocalHttpsServer.ResponseBody, tab.Response.RawText);
        Assert.Equal(appWideVerifySsl, App.Services.GetRequiredService<SettingsService>().Current.VerifySsl);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Opening_request_settings_shows_and_expands_the_section()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());
        var (window, tab) = await SendToSelfSignedServerAsync(server);
        Assert.False(tab.Sections.IsSettingsSectionVisible);
        var revealRequests = 0;
        tab.Sections.SettingsRevealRequested += () => revealRequests++;

        var openSettingsButton = Find<Button>(window, "OpenRequestSettingsButton");
        openSettingsButton.Command!.Execute(openSettingsButton.CommandParameter);
        UiTestHelpers.Pump();

        Assert.True(tab.Sections.IsSettingsSectionVisible);
        Assert.True(tab.Sections.SettingsExpanded);
        Assert.Equal(1, revealRequests);
        Assert.True(Find<Border>(window, "SettingsSection").IsEffectivelyVisible);
        Assert.Null(tab.Settings.VerifySslOverride);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Selectable_text_with_the_mono_class_uses_the_monospace_font()
    {
        using var listener = new PlainHttpListener();
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.UrlEditor.Url = $"https://127.0.0.1:{listener.Port}/";

        await tab.Sending.SendCommand.ExecuteAsync(null);
        UiTestHelpers.Pump();

        Assert.Equal(RequestFailureKind.PlainHttpAnswered, tab.Response.Failure?.Model.Kind);
        Find<CollapsibleSection>(window, "FailureDetailsSection").IsExpanded = true;
        UiTestHelpers.Pump();
        var originalMessage = Find<SelectableTextBlock>(window, "FailureOriginalMessageText");
        Assert.Contains("mono", originalMessage.Classes);
        Assert.True(Application.Current!.TryFindResource("MonoFont", out var monoFont));
        Assert.Equal(monoFont, originalMessage.FontFamily);

        // Any selectable text with the class, as in the headers table and the resolved URL, gets the font too.
        var plainSelectableText = new SelectableTextBlock { Text = "sample", Classes = { "mono" } };
        window.GetVisualDescendants().OfType<Panel>().First().Children.Add(plainSelectableText);
        UiTestHelpers.Pump();
        Assert.Equal(monoFont, plainSelectableText.FontFamily);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Every_failure_action_is_handled_by_the_request_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        // "Open settings" would show the real dialog, which the main window installs when it is created.
        var services = App.Services.GetRequiredService<TabServices>();
        var originalOpenSettings = services.OpenSettingsAsync;
        services.OpenSettingsAsync = () => Task.CompletedTask;
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Response.SetFailure(RequestFailure.FromMessage("A failure to act on."));

        foreach (var action in Enum.GetValues<RequestFailureAction>())
        {
            var exception = Record.Exception(() => tab.Response.RunFailureActionCommand.Execute(action));
            Assert.True(exception is null, $"{action} is not handled by the request tab: {exception?.Message}");
        }

        // Turning verification off, retrying and choosing a file start work; let it finish before the window goes.
        await UiTestHelpers.WaitUntil(() => !tab.Sending.IsSending && !tab.FailureActions.TurnOffVerificationAndResendCommand.IsRunning && !tab.FailureActions.ChooseBodyFileCommand.IsRunning);
        services.OpenSettingsAsync = originalOpenSettings;
        window.Close();
    }
}
