using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ExecutionProbe : IExecutionProbe
{
    public Guid ScopeId { get; } = Guid.NewGuid();
}
