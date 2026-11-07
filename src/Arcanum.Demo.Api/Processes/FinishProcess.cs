using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class FinishProcess(IExecutionProbe probe)
{
    public Task<ProcessResult<CompletedData>> ExecuteAsync(
        EnrichedData enriched,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new CompletedData(enriched.Text, probe.ScopeId, enriched.ScopeId == probe.ScopeId);
        return Task.FromResult(new ProcessResult<CompletedData>(result, "finished"));
    }
}
