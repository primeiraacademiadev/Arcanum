using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelOptionalJoinProcess
{
    [ReturnName("combined")]
    public ProcessResult<string> Execute(string alpha) =>
        new(alpha, "finished");
}
