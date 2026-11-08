using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelFaultAlphaProcess(ParallelFaultGate gate)
{
    [ReturnName("alpha")]
    public async Task<ProcessResult<string>> ExecuteAsync()
    {
        await gate.WaitForBothAsync();
        throw new InvalidOperationException("alpha failure");
    }
}
