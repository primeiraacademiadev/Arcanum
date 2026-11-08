using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Arcanum.Framework;

/// <summary>
/// Minimal HTTP adapter. It binds a Capability's declared contract, without application DTO mapping.
/// Never expose a demo Capability endpoint publicly without an authentication/authorization policy.
/// </summary>
public static class ArcanumEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapArcanumCapabilityEndpoint(
        this IEndpointRouteBuilder routes,
        string pattern = "/capabilities/{name}/execute",
        string? namePrefix = null)
    {
        return routes.MapPost(pattern, async Task<IResult> (
            string name,
            JsonElement payload,
            HttpContext context,
            CapabilityCatalog catalog,
            CapabilityJsonDispatcher dispatcher) =>
        {
            if ((namePrefix is not null && !name.StartsWith(namePrefix, StringComparison.Ordinal)) ||
                !catalog.Contains(name))
                return Results.NotFound();

            try
            {
                var result = await dispatcher.ExecuteAsync(
                    name, payload, context.RequestServices, context.RequestAborted);

                return result.Terminal switch
                {
                    "@complete" => Results.Ok(result),
                    "@stop" => Results.UnprocessableEntity(result),
                    "@fail" => Results.Problem(title: "Capability returned failure", statusCode: 500),
                    _ => Results.Problem(title: "Unsupported terminal directive", statusCode: 500)
                };
            }
            catch (CapabilityContractException e)
            {
                return Results.Problem(title: "Invalid Capability input", detail: e.Message, statusCode: 400);
            }
            catch (CapabilityExecutionLimitExceededException)
            {
                return Results.Problem(title: "Capability execution limit exceeded", statusCode: 500);
            }
            catch (CapabilityDepthLimitExceededException)
            {
                return Results.Problem(title: "Capability depth limit exceeded", statusCode: 500);
            }
        });
    }
}
