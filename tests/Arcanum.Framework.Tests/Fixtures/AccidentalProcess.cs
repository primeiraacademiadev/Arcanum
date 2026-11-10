using Arcanum.Demo.Api;
using Arcanum.Framework;

namespace Arcanum.Framework.Tests.Fixtures;

/// <summary>Not discoverable merely because of a naming convention.</summary>
public sealed class AccidentalProcess
{
    public ProcessResult<CounterState> Execute(CounterState state) => new(state, "done");
}
