using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class FirstValueProcess
{
    public ProcessResult<CustomerValue> Execute(DemoInput request) =>
        new(new CustomerValue("customer"), "first");
}
