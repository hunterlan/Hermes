using System.Text.RegularExpressions;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public partial class ManualTests
{
    private static ManualService Manual => App.Services.GetRequiredService<ManualService>();

    private static (Views.MainWindow Window, MainWindowViewModel ViewModel) ShowEmptyWindow()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        return (window, vm);
    }

    [AvaloniaFact]
    public void F1_opens_one_manual_tab_and_focuses_it_the_second_time()
    {
        var (window, vm) = ShowEmptyWindow();

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        var help = Assert.IsType<HelpTabViewModel>(vm.ActiveTab);
        vm.NewRequestCommand.Execute(null);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);

        Assert.Same(help, vm.ActiveTab);
        Assert.Single(vm.Tabs.OfType<HelpTabViewModel>());
        window.Close();
    }

    [AvaloniaFact]
    public void Palette_lists_the_manual()
    {
        var (window, vm) = ShowEmptyWindow();

        vm.OpenCommandPaletteCommand.Execute(null);
        vm.CommandPalette.Query = "manual";
        var hit = Assert.Single(vm.CommandPalette.Results);
        vm.CommandPalette.Run(hit);

        Assert.IsType<HelpTabViewModel>(vm.ActiveTab);
        window.Close();
    }

    [AvaloniaFact]
    public void Manual_tab_shows_a_page_and_follows_links_between_pages()
    {
        var (window, vm) = ShowEmptyWindow();

        vm.OpenManual();
        var help = Assert.IsType<HelpTabViewModel>(vm.ActiveTab);
        help.OpenLinkCommand.Execute("09-settings.md");

        Assert.Equal("09-settings.md", help.SelectedPage?.FileName);
        Assert.StartsWith("# Settings", help.Markdown);
        window.Close();
    }

    [AvaloniaFact]
    public void Every_page_in_the_index_is_embedded()
    {
        Assert.NotEmpty(Manual.Pages);
        foreach (var page in Manual.Pages)
            Assert.True(Manual.Exists(page.FileName), page.FileName + " is missing from the embedded manual.");
    }

    [AvaloniaFact]
    public void Links_between_manual_pages_resolve()
    {
        var files = Manual.Pages.Select(page => page.FileName).Append(ManualService.IndexFileName);
        foreach (var file in files)
        {
            foreach (Match link in RelativeLink().Matches(Manual.Read(file)))
                Assert.True(Manual.Exists(link.Groups["file"].Value), $"{file} links to missing {link.Value}.");
        }
    }

    [GeneratedRegex(@"\]\((?<file>[^)#:/]+\.md)(#[^)]*)?\)")]
    private static partial Regex RelativeLink();
}
