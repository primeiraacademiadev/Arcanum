using Arcanum.Framework;

namespace Arcanum.Demo.Api;

// No IProcess, naming suffix or attribute: selected exclusively by Capability JSON.
public sealed class EchoActivity
{
    [ReturnName("echo")]
    public ProcessResult<DemoInput> Execute(DemoInput request) =>
        new(request, "echoed");
}
