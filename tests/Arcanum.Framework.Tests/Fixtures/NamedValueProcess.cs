using Arcanum.Demo.Api;
using Arcanum.Framework;

namespace Arcanum.Framework.Tests.Fixtures;

/// <summary>Tests optional named data binding when more than one object has the same type.</summary>
public sealed class NamedValueProcess
{
    public ProcessResult<CounterState> Execute(
        [FromCapabilityData("chosen")] CounterState value) =>
        new(value, "selected");
}
