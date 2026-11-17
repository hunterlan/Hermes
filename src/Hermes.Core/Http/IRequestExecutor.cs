using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>Sends a prepared request and reports what came back, a failure, or that it was cancelled.</summary>
public interface IRequestExecutor
{
    Task<ExecutionResult> ExecuteAsync(PreparedRequest request, CancellationToken cancellationToken);
}
