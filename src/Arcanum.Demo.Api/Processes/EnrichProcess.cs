using Microsoft.Extensions.Logging;
using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class EnrichProcess(IExecutionProbe probe, ILogger<EnrichProcess> logger)
{
    public ValueTask<ProcessResult<EnrichedData>> ExecuteAsync(
        ValidationData validated,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (validated.ScopeId != probe.ScopeId)
            throw new InvalidOperationException("A Capability execution must share scoped resources.");

        logger.LogInformation("Enriching validated demo input");
        var value = validated.Value!.Trim().ToUpperInvariant();
        return ValueTask.FromResult(new ProcessResult<EnrichedData>(
            new EnrichedData(value, probe.ScopeId), "enriched"));
    }
}
