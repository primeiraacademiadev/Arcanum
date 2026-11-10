using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelBetaProcess
{
    [ReturnName("beta")]
    public async Task<ProcessResult<string>> ExecuteAsync(
        ValidationData validated, CancellationToken cancellationToken)
    {
        await Task.Delay(130, cancellationToken);
        return new(validated.Value!.Trim().ToLowerInvariant(), "done");
    }
}
