using Arcanum.Framework;

namespace Arcanum.Demo.Api;

/// <summary>Stateless process. Repeated visits are represented by execution data.</summary>
public sealed class CounterProcess
{
    public ProcessResult<CounterState> Execute(
        DemoInput request,
        CounterState current)
    {
        var next = new CounterState(current.Count + 1);
        return new(next, next.Count >= request.Iterations ? "complete" : "again");
    }
}
