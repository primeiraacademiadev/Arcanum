using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelFaultBetaProcess(ParallelFaultGate gate)
{
    [ReturnName("beta")]
    public async Task<ProcessResult<string>> ExecuteAsync(CancellationToken cancellationToken)
    {
        await gate.WaitForBothAsync();
        // Deliberately non-cooperative to prove that every independently
        // thrown exception is logged even AFTER the first has cancelled the run.
        await Task.Delay(70);
        if (!cancellationToken.IsCancellationRequested)
            throw new InvalidOperationException("beta did not observe cancellation");
        throw new InvalidOperationException("beta failure after cancellation");
    }
}
