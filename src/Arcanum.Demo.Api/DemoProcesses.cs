using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed record DemoInput(string? Value, int Iterations = 1);
public sealed record ValidationData(string? Value, Guid ScopeId);
public sealed record EnrichedData(string Text, Guid ScopeId);
public sealed record CompletedData(string Text, Guid ScopeId, bool SameScopedResource);
public sealed record CounterState(int Count);

public interface IExecutionProbe
{
    Guid ScopeId { get; }
}

public sealed class ExecutionProbe : IExecutionProbe
{
    public Guid ScopeId { get; } = Guid.NewGuid();
}

public sealed class ValidateProcess(IExecutionProbe probe) : IProcess
{
    public ProcessResult<ValidationData> Execute(
        [FromCapabilityData("request")] DemoInput request)
    {
        return new(
            new ValidationData(request.Value, probe.ScopeId),
            string.IsNullOrWhiteSpace(request.Value) ? "invalid" : "valid");
    }
}

public sealed class EnrichProcess(IExecutionProbe probe) : IProcess
{
    public ValueTask<ProcessResult<EnrichedData>> ExecuteAsync(
        [FromCapabilityData("validated")] ValidationData validated,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (validated.ScopeId != probe.ScopeId)
            throw new InvalidOperationException("A Capability execution must share scoped resources.");

        var value = validated.Value!.Trim().ToUpperInvariant();
        return ValueTask.FromResult(new ProcessResult<EnrichedData>(
            new EnrichedData(value, probe.ScopeId), "enriched"));
    }
}

public sealed class FinishProcess(IExecutionProbe probe) : IProcess
{
    public Task<ProcessResult<CompletedData>> ExecuteAsync(
        [FromCapabilityData("enriched")] EnrichedData enriched,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new CompletedData(enriched.Text, probe.ScopeId, enriched.ScopeId == probe.ScopeId);
        return Task.FromResult(new ProcessResult<CompletedData>(result, "finished"));
    }
}

/// <summary>Stateless implementation; repeated node visits are tracked in execution data, not fields.</summary>
public sealed class CounterProcess : IProcess
{
    public ProcessResult<CounterState> Execute(
        [FromCapabilityData("request")] DemoInput request,
        [FromCapabilityData("counter")] CounterState current)
    {
        var next = new CounterState(current.Count + 1);
        return new(next, next.Count >= request.Iterations ? "complete" : "again");
    }
}
