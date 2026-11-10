using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public interface IExecutionProbe
{
    Guid ScopeId { get; }
}
