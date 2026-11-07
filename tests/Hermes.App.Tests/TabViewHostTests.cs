using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.ViewModels;
using Hermes.App.Views;

namespace Hermes.App.Tests;

/// <summary>The pool of tab views behind the tab strip (issue #28).</summary>
public class TabViewHostTests
{
    [AvaloniaFact]
    public void Switching_back_to_a_recent_tab_shows_its_own_view_again()
    {
        var (window, vm, host) = ShowWindow(poolSize: 3);
        var first = vm.ActiveTab!;
        var firstView = ActiveView(host);
        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        var secondView = ActiveView(host);

        vm.ActiveTab = first;
        UiTestHelpers.Pump();

        Assert.NotSame(firstView, secondView);
        Assert.Same(firstView, ActiveView(host));
        Assert.Same(first, firstView.DataContext);
        Assert.False(secondView.IsEffectivelyVisible);
        Close(window, vm);
    }

    [AvaloniaFact]
    public void A_full_pool_hands_the_least_recently_used_view_to_the_new_tab()
    {
        var (window, vm, host) = ShowWindow(poolSize: 2);
        var oldest = vm.ActiveTab!;
        var oldestView = ActiveView(host);
        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        var recent = vm.ActiveTab!;

        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();

        Assert.Equal(2, host.LiveViewCount);
        Assert.Equal(2, RequestViews(host).Count);
        Assert.Same(oldestView, ActiveView(host));
        Assert.Same(vm.ActiveTab, oldestView.DataContext);
        Assert.NotSame(oldest, oldestView.DataContext);

        // The recent tab kept its view; the oldest one gets a view back when it's opened again.
        vm.ActiveTab = recent;
        UiTestHelpers.Pump();
        Assert.Same(recent, ActiveView(host).DataContext);
        vm.ActiveTab = oldest;
        UiTestHelpers.Pump();
        Assert.Same(oldest, ActiveView(host).DataContext);
        Assert.Equal(2, RequestViews(host).Count);
        Close(window, vm);
    }

    [AvaloniaFact]
    public void Closing_a_tab_drops_its_view()
    {
        var (window, vm, host) = ShowWindow(poolSize: 3);
        var closing = vm.ActiveTab!;
        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Equal(2, host.LiveViewCount);

        vm.RemoveTab(closing);
        UiTestHelpers.Pump();

        Assert.Equal(1, host.LiveViewCount);
        Assert.DoesNotContain(RequestViews(host), view => ReferenceEquals(view.DataContext, closing));
        Close(window, vm);
    }

    [AvaloniaFact]
    public void Lowering_the_pool_size_drops_the_oldest_views()
    {
        var (window, vm, host) = ShowWindow(poolSize: 3);
        vm.NewRequestCommand.Execute(null);
        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Equal(3, host.LiveViewCount);

        host.MaxLiveViews = 1;
        UiTestHelpers.Pump();

        Assert.Equal(1, host.LiveViewCount);
        Assert.Same(vm.ActiveTab, ActiveView(host).DataContext);
        Close(window, vm);
    }

    [AvaloniaFact]
    public void Ctrl_L_focuses_the_url_of_the_tab_on_screen()
    {
        var (window, vm, host) = ShowWindow(poolSize: 3);
        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();

        window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
        UiTestHelpers.Pump();

        var urlBox = ActiveView(host).FindControl<VariableTextBox>("UrlBox")!;
        Assert.True(urlBox.IsKeyboardFocusWithin);
        Close(window, vm);
    }

    [AvaloniaFact]
    public void Typing_after_a_switch_never_reaches_the_hidden_tab()
    {
        var (window, vm, host) = ShowWindow(poolSize: 3);
        var first = (RequestTabViewModel)vm.ActiveTab!;
        ActiveView(host).FocusUrl();
        UiTestHelpers.Pump();
        window.KeyTextInput("a");
        UiTestHelpers.Pump();
        var firstUrl = first.Url;
        Assert.EndsWith("a", firstUrl);

        vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        window.KeyTextInput("b");
        UiTestHelpers.Pump();

        Assert.Equal(firstUrl, first.Url);
        Assert.DoesNotContain(RequestViews(host).Where(view => !view.IsEffectivelyVisible),
            view => view.GetVisualDescendants().OfType<InputElement>().Any(element => element.IsFocused));
        Close(window, vm);
    }

    private static (MainWindow Window, MainWindowViewModel ViewModel, TabViewHost Host) ShowWindow(int poolSize)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var host = window.FindControl<TabViewHost>("TabViews")!;
        host.MaxLiveViews = poolSize;
        UiTestHelpers.Pump();
        return (window, vm, host);
    }

    private static RequestTabView ActiveView(TabViewHost host) =>
        RequestViews(host).Single(view => view.IsEffectivelyVisible);

    private static List<RequestTabView> RequestViews(TabViewHost host) =>
        host.GetVisualDescendants().OfType<RequestTabView>().ToList();

    private static void Close(Window window, MainWindowViewModel vm)
    {
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        window.Close();
    }
}
