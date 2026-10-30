using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class EnvironmentMatrixTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public void Matrix_edits_every_environment_and_warns_about_empty_values()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var key = "matrixVar" + Guid.NewGuid().ToString("N")[..8];
        var staging = Workspace.CreateEnvironment("Staging " + Guid.NewGuid());
        staging.Variables.Add(new Variable(key, ""));
        Workspace.SaveEnvironment(staging);
        var production = Workspace.CreateEnvironment("Production " + Guid.NewGuid());
        production.Variables.Add(new Variable(key, "prod-value"));
        Workspace.SaveEnvironment(production);
        var collection = Workspace.CreateCollection("Matrix " + Guid.NewGuid());
        Workspace.AddNode(collection, null, new HttpRequestModel { Name = "Uses it", Url = "https://x/{{" + key + "}}" });
        Workspace.ActiveEnvironment = staging;

        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        var row = Assert.Single(matrix.Rows, r => r.Name == key);
        Assert.Equal(1, row.UsedBy);
        Assert.Contains(key, matrix.MissingWarning);
        UiTestHelpers.Screenshot(window, "14-environment-matrix");

        var stagingCell = row.Cells.Single(c => c.Column.Model == staging);
        stagingCell.Value = "stg-value";
        Assert.True(matrix.IsDirty);
        Assert.Null(matrix.MissingWarning);
        matrix.SaveAsync().Wait();

        Assert.False(matrix.IsDirty);
        Assert.Equal("stg-value", staging.Variables.Single(v => v.Key == key).Value);
        Assert.Equal("prod-value", production.Variables.Single(v => v.Key == key).Value);

        Workspace.ActiveEnvironment = null;
        window.Close();
    }

    [AvaloniaFact]
    public void Matrix_minimum_width_grows_with_each_environment_column()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        const double fixedColumnsWidth = 190 + 110;
        const double environmentColumnMinWidth = 230;
        Assert.Equal(fixedColumnsWidth + matrix.Columns.Count * environmentColumnMinWidth, matrix.TableMinWidth);

        var widthBefore = matrix.TableMinWidth;
        Workspace.CreateEnvironment("Staging " + Guid.NewGuid());
        matrix.OnVariablesChanged();

        Assert.Equal(widthBefore + environmentColumnMinWidth, matrix.TableMinWidth);
        window.Close();
    }

    [AvaloniaFact]
    public void Matrix_keeps_secret_types_and_renames_variables()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var key = "matrixSecret" + Guid.NewGuid().ToString("N")[..8];
        var production = Workspace.CreateEnvironment("Production " + Guid.NewGuid());
        production.Variables.Add(new Variable(key, "prod-secret") { Type = VariableType.Secret });
        Workspace.SaveEnvironment(production);
        var staging = Workspace.CreateEnvironment("Staging " + Guid.NewGuid());

        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        var row = Assert.Single(matrix.Rows, r => r.Name == key);
        row.Cells.Single(c => c.Column.Model == staging).Value = "stg-secret";
        row.Name = key + "Renamed";
        matrix.SaveAsync().Wait();

        var added = Assert.Single(staging.Variables);
        Assert.Equal((key + "Renamed", VariableType.Secret), (added.Key, added.Type));
        var renamed = Assert.Single(production.Variables);
        Assert.Equal((key + "Renamed", "prod-secret"), (renamed.Key, renamed.Value));

        Workspace.DeleteEnvironment(production);
        Workspace.DeleteEnvironment(staging);
        window.Close();
    }

    [AvaloniaFact]
    public void Matrix_swaps_variable_names()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var (a, b) = ("swapA" + Guid.NewGuid().ToString("N")[..8], "swapB" + Guid.NewGuid().ToString("N")[..8]);
        var env = Workspace.CreateEnvironment("Swap " + Guid.NewGuid());
        env.Variables.AddRange([new Variable(a, "value of a"), new Variable(b, "value of b")]);
        Workspace.SaveEnvironment(env);

        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        var (rowA, rowB) = (matrix.Rows.Single(r => r.Name == a), matrix.Rows.Single(r => r.Name == b));
        rowA.Name = b;
        rowB.Name = a;
        matrix.SaveAsync().Wait();

        Assert.Equal("value of a", env.Variables.Single(v => v.Key == b).Value);
        Assert.Equal("value of b", env.Variables.Single(v => v.Key == a).Value);
        Workspace.DeleteEnvironment(env);
        window.Close();
    }

    [AvaloniaFact]
    public void Saving_the_matrix_does_not_bring_back_a_deleted_environment()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var doomed = Workspace.CreateEnvironment("Doomed " + Guid.NewGuid());
        doomed.Variables.Add(new Variable("matrixDoomed" + Guid.NewGuid().ToString("N")[..8], "v"));
        Workspace.SaveEnvironment(doomed);

        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        matrix.Rows.First().Cells.First(c => c.Column.Model == doomed).Value = "edited";
        Assert.True(matrix.IsDirty);
        Workspace.DeleteEnvironment(doomed);
        matrix.SaveAsync().Wait();

        Assert.DoesNotContain(matrix.Columns, c => c.Model == doomed);
        Assert.False(File.Exists(Path.Combine(Workspace.DataDirectory, "environments", $"{doomed.Id}.json")));
        window.Close();
    }
}
