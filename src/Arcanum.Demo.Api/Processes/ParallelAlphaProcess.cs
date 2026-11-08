using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelAlphaProcess
{
    [ReturnName("alpha")]
    public async Task<ProcessResult<string>> ExecuteAsync(
        ValidationData validated, CancellationToken cancellationToken)
    {
        await Task.Delay(90, cancellationToken);
        return new(validated.Value!.Trim().ToUpperInvariant(), "done");
    }
}
