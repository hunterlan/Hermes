using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.Core.Import;

public sealed class ImportResult
{
    public List<CollectionModel> Collections { get; } = [];
    public List<EnvironmentModel> Environments { get; } = [];
    public List<HttpRequestModel> Requests { get; } = [];
    /// <summary>What couldn't be imported, in plain words, with the raw text of any exception kept as details.</summary>
    public List<StorageError> Errors { get; } = [];

    public bool IsEmpty => Collections.Count == 0 && Environments.Count == 0 && Requests.Count == 0;
}
