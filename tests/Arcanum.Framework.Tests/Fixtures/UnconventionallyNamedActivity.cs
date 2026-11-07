using Arcanum.Demo.Api;
using Arcanum.Framework;

namespace Arcanum.Framework.Tests.Fixtures;

/// <summary>Deliberately does not end in Process: identity is the IProcess contract.</summary>
public sealed class UnconventionallyNamedActivity : IProcess
{
    public ProcessResult<CounterState> Execute(CounterState state) => new(state, "done");
}
