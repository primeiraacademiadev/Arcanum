using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ValueSummaryProcess
{
    public ProcessResult<string> Execute(IPolymorphicValue value) =>
        new(value.Kind, "summarized");
}
