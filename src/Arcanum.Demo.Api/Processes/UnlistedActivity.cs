using Arcanum.Framework;

namespace Arcanum.Demo.Api;

/// <summary>Not referenced by any Capability JSON; never registered as a Process.</summary>
public sealed class UnlistedActivity
{
    public ProcessResult<DemoInput> Execute(DemoInput request) =>
        new(request, "ignored");
}
