using Microsoft.Extensions.Logging;
using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed class ValidateProcess(IExecutionProbe probe, ILogger<ValidateProcess> logger)
{
    public ProcessResult<ValidationData> Execute(
        DemoInput request)
    {
        logger.LogInformation("Validating demo input for scoped execution {ScopeId}", probe.ScopeId);
        return new(
            new ValidationData(request.Value, probe.ScopeId),
            string.IsNullOrWhiteSpace(request.Value) ? "invalid" : "valid");
    }
}
