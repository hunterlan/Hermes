using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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
    private sealed class PickerScope : IDisposable
    {
        private readonly List<EnvironmentModel> _environments = [];
        private readonly List<Variable> _globals = [];

        public PickerScope() => (Window, ViewModel) = UiTestHelpers.ShowMainWindow();

        public Window Window { get; }
        public MainWindowViewModel ViewModel { get; }

        public EnvironmentModel CreateEnvironment(params Variable[] variables)
        {
            var env = Workspace.CreateEnvironment("Picker " + Guid.NewGuid());
            env.Variables.AddRange(variables);
            Workspace.SaveEnvironment(env);
            _environments.Add(env);
            return env;
        }

        /// <summary>Adds globals ahead of the existing ones, so they are among the rows the picker shows.</summary>
        public void AddGlobals(params Variable[] variables)
        {
            Workspace.Globals.Variables.InsertRange(0, variables);
            Workspace.SaveEnvironment(Workspace.Globals);
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

        public void Dispose()
        {
            EnvironmentButton.Flyout!.Hide();
            Workspace.ActiveEnvironment = null;
            foreach (var env in _environments)
                Workspace.DeleteEnvironment(env);
            if (_globals.Count > 0)
            {
                Workspace.Globals.Variables.RemoveAll(_globals.Contains);
                Workspace.SaveEnvironment(Workspace.Globals);
            }
            Window.Close();
        }
    }

    [AvaloniaFact]
    public void Environment_section_shows_five_rows_and_counts_the_rest()
    {
        using var scope = new PickerScope();
        var env = scope.CreateEnvironment(Enumerable.Range(1, 7).Select(i => new Variable("key" + i, "value" + i)).ToArray());
        env.Variables.Add(new Variable("disabled", "x") { Enabled = false });
        Workspace.SaveEnvironment(env);

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
    public void Globals_section_is_capped_too()
    {
        using var scope = new PickerScope();
        scope.AddGlobals(Enumerable.Range(1, 7).Select(_ => new Variable("glob" + Guid.NewGuid(), "v")).ToArray());

        var globals = scope.ViewModel.QuickLookGlobals;
        Assert.Equal(MainWindowViewModel.QuickLookLimit, globals.Rows.Count);
        Assert.True(globals.Total >= 7);
        Assert.Equal(globals.Total - MainWindowViewModel.QuickLookLimit, globals.MoreCount);
        Assert.Equal($"Globals · {globals.Total}", scope.ViewModel.QuickLookGlobalsTitle);
    }

    [AvaloniaFact]
    public void Global_overridden_by_the_active_environment_is_flagged()
    {
        using var scope = new PickerScope();
        var key = "shared" + Guid.NewGuid();
        scope.AddGlobals(new Variable(key, "global"));
        var env = scope.CreateEnvironment(new Variable(key, "env"));
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
    public void No_environment_selected()
    {
        using var scope = new PickerScope();
        var vm = scope.ViewModel;

        scope.Activate(null);

        Assert.False(vm.HasActiveEnvironment);
        Assert.Equal("None", vm.EnvironmentButtonText);
        var none = vm.EnvironmentOptions[0];
        Assert.Equal("No environment", none.Name);
        Assert.True(none.IsNone);
        Assert.True(none.IsActive);
        Assert.True(vm.QuickLookEnvironment.IsEmpty);
    }

    [AvaloniaFact]
    public void Popover_keeps_its_width_with_long_values()
    {
        using var scope = new PickerScope();
        scope.Activate(scope.CreateEnvironment(
            new Variable(new string('k', 120), "https://api.example.com/" + new string('v', 200))));

        scope.OpenPicker();

        var content = scope.Window.FindControl<Control>("EnvironmentPicker")!;
        var presenter = Assert.IsType<FlyoutPresenter>(content.FindAncestorOfType<FlyoutPresenter>());
        Assert.Equal(400, presenter.Bounds.Width);
        Assert.DoesNotContain(presenter.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsVisible);
        UiTestHelpers.Screenshot(scope.Window, "40-environment-picker");
    }

    [AvaloniaFact]
    public void Rows_show_the_sent_value_masked_and_flag_empty_ones()
    {
        using var scope = new PickerScope();
        var env = scope.CreateEnvironment(
            new Variable("current", "initial") { CurrentValue = "current" },
            new Variable("secret", "hunter2") { Type = VariableType.Secret },
            new Variable("blank", ""));
        env.ConfirmBeforeSend = true;
        Workspace.SaveEnvironment(env);

        scope.Activate(env);

        var rows = scope.ViewModel.QuickLookEnvironment.Rows.ToDictionary(r => r.Key);
        Assert.Equal("current", rows["current"].Value);
        Assert.Equal(VariableScope.MaskedValue, rows["secret"].Value);
        Assert.True(rows["blank"].IsEmpty);
        Assert.False(rows["current"].IsEmpty);
        Assert.True(scope.ViewModel.EnvironmentOptions.Single(o => o.Model == env).AsksFirst);
    }
}
