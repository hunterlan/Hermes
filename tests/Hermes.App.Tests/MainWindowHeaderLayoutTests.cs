using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The header row must not overlap when the window is narrow (issue #52).</summary>
public class MainWindowHeaderLayoutTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaTheory]
    [InlineData(900)]
    [InlineData(1000)]
    [InlineData(1059)]
    [InlineData(1100)]
    [InlineData(1199)]
    [InlineData(1440)]
    public void Header_items_do_not_overlap_and_side_groups_fit_their_thirds(double windowWidth)
    {
        var previousEnvironment = Workspace.ActiveEnvironment;
        var env = CreateConfirmingEnvironment();
        var vm = new MainWindowViewModel(App.Services.GetRequiredService<TabServices>());
        var window = new MainWindow { DataContext = vm, Width = windowWidth, Height = 600 };
        try
        {
            window.Show();
            UiTestHelpers.Pump();
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == env);
            UiTestHelpers.Pump();
            Assert.True(vm.ActiveEnvironmentConfirms);

            var importButton = window.FindControl<Button>("ImportButton")!;
            Assert.Equal("Import", AutomationProperties.GetName(importButton));
            var importBounds = BoundsIn(importButton, window);
            var searchBounds = BoundsIn(window.FindControl<Button>("SearchButton")!, window);
            var environmentBounds = BoundsIn(window.FindControl<Button>("EnvironmentButton")!, window);

            Assert.True(importBounds.Right <= searchBounds.Left, $"Import overlaps search at {windowWidth}px.");
            Assert.True(searchBounds.Right <= environmentBounds.Left, $"Search overlaps environment at {windowWidth}px.");
            Assert.True(environmentBounds.Right <= window.Bounds.Width, $"Environment is off-window at {windowWidth}px.");
            Assert.True(searchBounds.Width > 0, $"Search box collapsed at {windowWidth}px.");

            // Side groups don't clip, so each must fit its third or it spills into the search box.
            var headerGrid = window.FindControl<Grid>("HeaderGrid")!;
            var leading = window.FindControl<StackPanel>("HeaderLeadingGroup")!;
            var trailing = window.FindControl<StackPanel>("HeaderTrailingGroup")!;
            Assert.True(leading.DesiredSize.Width <= headerGrid.ColumnDefinitions[0].ActualWidth,
                $"Leading group ({leading.DesiredSize.Width}px) is wider than its third at {windowWidth}px.");
            Assert.True(trailing.DesiredSize.Width <= headerGrid.ColumnDefinitions[2].ActualWidth,
                $"Trailing group ({trailing.DesiredSize.Width}px) is wider than its third at {windowWidth}px.");
        }
        finally
        {
            Workspace.ActiveEnvironment = previousEnvironment;
            Workspace.DeleteEnvironment(env);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Search_box_is_420_wide_and_centred_in_the_window_when_there_is_room()
    {
        var vm = new MainWindowViewModel(App.Services.GetRequiredService<TabServices>());
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        try
        {
            window.Show();
            UiTestHelpers.Pump();

            var searchBounds = BoundsIn(window.FindControl<Button>("SearchButton")!, window);

            Assert.Equal(420, searchBounds.Width, precision: 0);
            Assert.Equal(window.Bounds.Width / 2, (searchBounds.Left + searchBounds.Right) / 2, precision: 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static EnvironmentModel CreateConfirmingEnvironment()
    {
        var env = Workspace.CreateEnvironment("Test environment with a very long name " + Guid.NewGuid());
        env.ConfirmBeforeSend = true;
        Workspace.SaveEnvironment(env);
        return env;
    }

    private static Rect BoundsIn(Control control, Window window) =>
        new(control.TranslatePoint(new Point(), window)!.Value, control.Bounds.Size);
}
