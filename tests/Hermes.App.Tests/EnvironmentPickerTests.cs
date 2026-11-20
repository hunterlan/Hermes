using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The header environment picker.</summary>
public class EnvironmentPickerTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    /// <summary>
    /// A main window plus the environments and globals a test adds. Workspace state is shared across tests, so disposing
    /// deactivates, deletes and removes them again; an environment left active (one that asks before sending, say) would
    /// change how later tests send requests.
    /// </summary>
    private sealed class PickerScope : IAsyncDisposable
    {
        private readonly List<EnvironmentModel> _environments = [];
        private readonly List<Variable> _globals = [];

        public PickerScope() => (Window, ViewModel) = UiTestHelpers.ShowMainWindow();

        public Window Window { get; }
        public MainWindowViewModel ViewModel { get; }

        public async Task<EnvironmentModel> CreateEnvironmentAsync(params Variable[] variables)
        {
            var env = await Workspace.CreateEnvironmentAsync("Picker " + Guid.NewGuid());
            env.Variables.AddRange(variables);
            await Workspace.SaveEnvironmentAsync(env);
            _environments.Add(env);
            return env;
        }

        /// <summary>Adds globals ahead of the existing ones, so they are among the rows the picker shows.</summary>
        public async Task AddGlobalsAsync(params Variable[] variables)
        {
            Workspace.Globals.Variables.InsertRange(0, variables);
            await Workspace.SaveEnvironmentAsync(Workspace.Globals);
            _globals.AddRange(variables);
        }

        public void Activate(EnvironmentModel? env) =>
            ViewModel.SelectedEnvironment = ViewModel.EnvironmentOptions.Single(o => o.Model == env);

        public Button EnvironmentButton => Window.FindControl<Button>("EnvironmentButton")!;

        public void OpenPicker()
        {
            EnvironmentButton.Flyout!.ShowAt(EnvironmentButton);
            UiTestHelpers.Pump();
        }

        public async ValueTask DisposeAsync()
        {
            EnvironmentButton.Flyout!.Hide();
            Workspace.ActiveEnvironment = null;
            foreach (var env in _environments)
                await Workspace.DeleteEnvironmentAsync(env);
            if (_globals.Count > 0)
            {
                Workspace.Globals.Variables.RemoveAll(_globals.Contains);
                await Workspace.SaveEnvironmentAsync(Workspace.Globals);
            }
            Window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Environment_section_shows_five_rows_and_counts_the_rest()
    {
        await using var scope = new PickerScope();
        var env = await scope.CreateEnvironmentAsync(Enumerable.Range(1, 7).Select(i => new Variable("key" + i, "value" + i)).ToArray());
        env.Variables.Add(new Variable("disabled", "x") { Enabled = false });
        await Workspace.SaveEnvironmentAsync(env);

        scope.Activate(env);

        var vm = scope.ViewModel;
        Assert.Equal(MainWindowViewModel.QuickLookLimit, vm.QuickLookEnvironment.Rows.Count);
        Assert.Equal(2, vm.QuickLookEnvironment.MoreCount);
        Assert.Equal("+2 more in the matrix", vm.QuickLookEnvironment.MoreText);
        Assert.Equal($"{env.Name} · 7 variables", vm.QuickLookEnvironmentTitle);
        Assert.Equal(env.Name, vm.EnvironmentButtonText);
        Assert.True(vm.EnvironmentOptions.Single(o => o.Model == env).IsActive);
    }

    [AvaloniaFact]
    public async Task Globals_section_is_capped_too()
    {
        await using var scope = new PickerScope();
        await scope.AddGlobalsAsync(Enumerable.Range(1, 7).Select(_ => new Variable("glob" + Guid.NewGuid(), "v")).ToArray());

        var globals = scope.ViewModel.QuickLookGlobals;
        Assert.Equal(MainWindowViewModel.QuickLookLimit, globals.Rows.Count);
        Assert.True(globals.Total >= 7);
        Assert.Equal(globals.Total - MainWindowViewModel.QuickLookLimit, globals.MoreCount);
        Assert.Equal($"Globals · {globals.Total}", scope.ViewModel.QuickLookGlobalsTitle);
    }

    [AvaloniaFact]
    public async Task Global_overridden_by_the_active_environment_is_flagged()
    {
        await using var scope = new PickerScope();
        var key = "shared" + Guid.NewGuid();
        await scope.AddGlobalsAsync(new Variable(key, "global"));
        var env = await scope.CreateEnvironmentAsync(new Variable(key, "env"));
        var vm = scope.ViewModel;

        scope.Activate(null);
        Assert.False(vm.QuickLookGlobals.Rows.Single(r => r.Key == key).IsOverridden);

        scope.Activate(env);
        var row = vm.QuickLookGlobals.Rows.Single(r => r.Key == key);
        Assert.True(row.IsOverridden);
        Assert.Equal("global", row.Value);
        Assert.False(vm.QuickLookEnvironment.Rows.Single().IsOverridden);
    }

    [AvaloniaFact]
    public async Task No_environment_selected()
    {
        await using var scope = new PickerScope();
        var vm = scope.ViewModel;

        scope.Activate(null);

        Assert.False(vm.HasActiveEnvironment);
        Assert.Equal("None", vm.EnvironmentButtonText);
        var none = vm.EnvironmentOptions[0];
        Assert.Equal("No environment", none.Name);
        Assert.True(none.IsNone);
        Assert.True(none.IsActive);
        Assert.True(vm.QuickLookEnvironment.IsEmpty);

        scope.OpenPicker();
        UiTestHelpers.Screenshot(scope.Window, "41-environment-picker-none");
    }

    [AvaloniaFact]
    public async Task Popover_keeps_its_width_with_long_values()
    {
        await using var scope = new PickerScope();
        var env = await scope.CreateEnvironmentAsync(
            new Variable("baseUrl", "https://api.staging.payments.example"),
            new Variable(new string('k', 120), "https://api.example.com/" + new string('v', 200)),
            new Variable("clientSecret", "s3cr3t") { Type = VariableType.Secret },
            new Variable("customerId", ""));
        env.ConfirmBeforeSend = true;
        await Workspace.SaveEnvironmentAsync(env);
        scope.Activate(env);

        scope.OpenPicker();

        var content = scope.Window.FindControl<Control>("EnvironmentPicker")!;
        var presenter = Assert.IsType<FlyoutPresenter>(content.FindAncestorOfType<FlyoutPresenter>());
        Assert.Equal(400, presenter.Bounds.Width);
        Assert.DoesNotContain(presenter.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsVisible);
        UiTestHelpers.Screenshot(scope.Window, "40-environment-picker");

        var chevron = scope.Window.FindControl<Control>("EnvironmentChevron")!;
        Assert.IsType<RotateTransform>(chevron.RenderTransform);
        scope.EnvironmentButton.Flyout!.Hide();
        UiTestHelpers.Pump();
        Assert.Null(chevron.RenderTransform);
    }

    [AvaloniaFact]
    public async Task Arrows_move_focus_and_enter_activates_without_closing()
    {
        await using var scope = new PickerScope();
        var (window, vm) = (scope.Window, scope.ViewModel);
        var env = await scope.CreateEnvironmentAsync(new Variable("k", "v"));
        scope.Activate(null);

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        UiTestHelpers.Pump();
        var button = scope.EnvironmentButton;
        Assert.True(button.Flyout!.IsOpen);
        var list = window.FindControl<ListBox>("EnvironmentList")!;
        Assert.Same(vm.EnvironmentOptions[0], list.SelectedItem);

        var target = vm.EnvironmentOptions.IndexOf(vm.EnvironmentOptions.Single(o => o.Model == env));
        for (var i = 0; i < target; i++)
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Same(env, ((EnvironmentOption)list.SelectedItem!).Model);
        Assert.Null(Workspace.ActiveEnvironment);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.Same(env, Workspace.ActiveEnvironment);
        Assert.True(button.Flyout.IsOpen);
        Assert.True(((EnvironmentOption)list.SelectedItem!).IsActive);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiTestHelpers.Pump();
        Assert.False(button.Flyout.IsOpen);
    }

    [AvaloniaFact]
    public async Task Rows_show_the_sent_value_masked_and_flag_empty_ones()
    {
        await using var scope = new PickerScope();
        var env = await scope.CreateEnvironmentAsync(
            new Variable("current", "initial") { CurrentValue = "current" },
            new Variable("secret", "hunter2") { Type = VariableType.Secret },
            new Variable("blank", ""));
        env.ConfirmBeforeSend = true;
        await Workspace.SaveEnvironmentAsync(env);

        scope.Activate(env);

        var rows = scope.ViewModel.QuickLookEnvironment.Rows.ToDictionary(r => r.Key);
        Assert.Equal("current", rows["current"].Value);
        Assert.Equal(VariableScope.MaskedValue, rows["secret"].Value);
        Assert.True(rows["blank"].IsEmpty);
        Assert.False(rows["current"].IsEmpty);
        Assert.True(scope.ViewModel.EnvironmentOptions.Single(o => o.Model == env).AsksFirst);
    }
}
