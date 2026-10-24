using Hermes.Core.Models;

namespace Hermes.Core.Import;

public sealed class ImportResult
{
    public List<CollectionModel> Collections { get; } = [];
    public List<EnvironmentModel> Environments { get; } = [];
    public List<HttpRequestModel> Requests { get; } = [];
    public List<string> Errors { get; } = [];

    public bool IsEmpty => Collections.Count == 0 && Environments.Count == 0 && Requests.Count == 0;
}
