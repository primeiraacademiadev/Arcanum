using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class SecondValueProcess
{
    public ProcessResult<SupplierValue> Execute(DemoInput request) =>
        new(new SupplierValue("supplier"), "second");
}
