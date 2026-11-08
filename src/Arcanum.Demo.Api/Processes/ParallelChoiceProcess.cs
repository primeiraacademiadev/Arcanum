using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ParallelChoiceProcess
{
    [ReturnName("choice")]
    public ProcessResult<string> Execute(DemoInput request) =>
        new(request.Value ?? "", request.Value?.Contains("skip", StringComparison.OrdinalIgnoreCase) == true
            ? "skip" : "include");
}
