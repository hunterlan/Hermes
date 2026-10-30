using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

public class EnvironmentColorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());

    public static TheoryData<EnvironmentColor> AllColors => new(Enum.GetValues<EnvironmentColor>());

    [Fact]
    public void A_new_environment_has_no_colour()
    {
        Assert.Equal(EnvironmentColor.None, new EnvironmentModel().Color);
    }

    [Fact]
    public void An_environment_file_without_a_colour_loads_as_no_colour()
    {
        var environment = Deserialize("""{ "name": "Old", "variables": [], "confirmBeforeSend": true }""");

        Assert.Equal("Old", environment.Name);
        Assert.True(environment.ConfirmBeforeSend);
        Assert.Equal(EnvironmentColor.None, environment.Color);
    }

    [Fact]
    public void The_colour_is_stored_by_name()
    {
        var json = ModelCloner.Serialize(new EnvironmentModel { Name = "Prod", Color = EnvironmentColor.Red });

        Assert.Equal("Red", JsonNode.Parse(json)!["color"]!.GetValue<string>());
    }

    [Fact]
    public void No_colour_is_left_out_of_the_file()
    {
        var json = ModelCloner.Serialize(new EnvironmentModel { Name = "Scratch" });

        Assert.DoesNotContain("color", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"red\"", EnvironmentColor.Red)]
    [InlineData("\"TEAL\"", EnvironmentColor.Teal)]
    [InlineData("\"Magenta\"", EnvironmentColor.None)]
    [InlineData("\"3\"", EnvironmentColor.None)]
    [InlineData("\"\"", EnvironmentColor.None)]
    [InlineData("3", EnvironmentColor.None)]
    [InlineData("null", EnvironmentColor.None)]
    [InlineData("{ \"r\": 255 }", EnvironmentColor.None)]
    [InlineData("[\"Red\"]", EnvironmentColor.None)]
    public void Colour_names_are_read_leniently(string colorJson, EnvironmentColor expected)
    {
        var environment = Deserialize($$"""{ "name": "E", "color": {{colorJson}}, "variables": [{ "key": "a", "value": "1" }] }""");

        Assert.Equal(expected, environment.Color);
        Assert.Equal("1", Assert.Single(environment.Variables).Value);
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void Each_colour_round_trips_through_json(EnvironmentColor color)
    {
        var copy = ModelCloner.Clone(new EnvironmentModel { Name = "E", Color = color });

        Assert.Equal(color, copy.Color);
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void Each_colour_persists_across_restarts(EnvironmentColor color)
    {
        new WorkspaceRepository(_dir).SaveEnvironment(new EnvironmentModel { Name = "E", Color = color });

        var reloaded = Assert.Single(new WorkspaceRepository(_dir).LoadEnvironments());

        Assert.Equal(color, reloaded.Color);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static EnvironmentModel Deserialize(string json) =>
        JsonSerializer.Deserialize(json, HermesJsonContext.Default.EnvironmentModel)!;
}
