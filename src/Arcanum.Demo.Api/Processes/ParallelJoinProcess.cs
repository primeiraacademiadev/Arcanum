using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelJoinProcess
{
    [ReturnName("combined")]
    public ProcessResult<string> Execute(string alpha, string beta) =>
        new($"{alpha}|{beta}", "finished");
}
