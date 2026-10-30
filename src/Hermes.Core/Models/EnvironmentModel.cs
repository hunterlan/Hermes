using System.Text.Json.Serialization;
using Hermes.Core.Serialization;

namespace Hermes.Core.Models;

public sealed class EnvironmentModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Variable> Variables { get; set; } = [];

    /// <summary>Ask before sending while this environment is active (e.g. Production).</summary>
    public bool ConfirmBeforeSend { get; set; }

    /// <summary>Swatch shown next to the name. Decoration only: it never implies <see cref="ConfirmBeforeSend"/>.</summary>
    [JsonConverter(typeof(EnvironmentColorJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public EnvironmentColor Color { get; set; }
}
