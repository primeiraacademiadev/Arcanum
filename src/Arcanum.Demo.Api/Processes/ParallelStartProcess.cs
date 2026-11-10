using Arcanum.Framework;

namespace Arcanum.Demo.Api;

// No Process result spawns a branch. The graph alone declares parallel edges.
public sealed class ParallelStartProcess
{
    [ReturnName("forked")]
    public ProcessResult<ValidationData> Execute(ValidationData validated) =>
        new(validated, "ready");
}
