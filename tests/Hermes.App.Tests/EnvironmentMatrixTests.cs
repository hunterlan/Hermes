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
}
