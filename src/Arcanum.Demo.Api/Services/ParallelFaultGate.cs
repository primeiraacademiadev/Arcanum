namespace Arcanum.Demo.Api;

// Test fixture: synchronizes both concurrent Processes before either throws.
// This ensures the second Process is in flight when the first faults.
public sealed class ParallelFaultGate
{
    private readonly TaskCompletionSource _bothStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public Task WaitForBothAsync()
    {
        if (Interlocked.Increment(ref _arrivals) == 2)
            _bothStarted.TrySetResult();
        return _bothStarted.Task;
    }
}
