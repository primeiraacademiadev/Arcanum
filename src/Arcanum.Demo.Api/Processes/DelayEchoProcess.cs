using Arcanum.Framework;

namespace Arcanum.Demo.Api;

/// <summary>Slow cancellable Process used to verify a Capability-wide deadline.</summary>
public sealed class DelayEchoProcess
{
    public async Task<ProcessResult<DemoInput>> ExecuteAsync(
        DemoInput request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
        return new(request, "echoed");
    }
}
