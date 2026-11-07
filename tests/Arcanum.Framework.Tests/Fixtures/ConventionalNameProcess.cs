using Arcanum.Demo.Api;
using Arcanum.Framework;

namespace Arcanum.Framework.Tests.Fixtures;

/// <summary>Two compatible values exist; the parameter name chooses the intended one.</summary>
public sealed class ConventionalNameProcess : IProcess
{
    public ProcessResult<CounterState> Execute(CounterState chosen) =>
        new(chosen, "selected");
}
