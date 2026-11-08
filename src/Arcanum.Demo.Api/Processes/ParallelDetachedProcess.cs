using Arcanum.Framework;

namespace Arcanum.Demo.Api;

// This branch remains active AFTER the other branch can safely pass its join.
public sealed class ParallelDetachedProcess
{
    [ReturnName("detached")]
    public async Task<ProcessResult<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(260, cancellationToken);
        return new("detached", "finished");
    }
}
