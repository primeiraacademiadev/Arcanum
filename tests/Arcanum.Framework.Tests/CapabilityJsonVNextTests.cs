using System.Text.Json;
using Arcanum.Demo.Api;
using Arcanum.Framework;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Arcanum.Framework.Tests;

public sealed class CapabilityJsonVNextTests
{
    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExecutionProbe, ExecutionProbe>();
        services.AddArcanumFramework(typeof(EchoActivity).Assembly);
        services.AddArcanumCapabilitiesFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "capabilities"));
        var provider = services.BuildServiceProvider(validateScopes: true);
        _ = provider.GetRequiredService<CapabilityCatalog>();
        return provider;
    }

    private static CapabilityDefinition Demo() => CapabilityCatalog.ParseJson(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "capabilities", "demo.capability-json.json")));

    [Fact]
    public async Task Template_is_reused_in_two_nodes_and_implicit_response_comes_from_context()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        using var json = JsonDocument.Parse("""{"value":"reuse"}""");
        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>().ExecuteAsync(
            "demo.capability-json", json.RootElement, scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("echoed", result.Intention);
        Assert.Equal(2, result.Steps);
        Assert.Equal("reuse", Assert.IsType<DemoInput>(result.Data).Value);
        var starts = result.Breadcrumbs.Where(b => b.Kind == "Started").ToArray();
        Assert.Equal(["first", "second"], starts.Select(x => x.NodeId).ToArray());
        Assert.NotEqual(starts[0].NodeExecutionId, starts[1].NodeExecutionId);
    }

    [Fact]
    public void CLR_contract_is_authoritative_and_template_drift_fails_during_startup()
    {
        using var provider = Provider();
        var definition = Demo();
        definition.Templates["echo"].Inputs["request"] = "MissingModel";
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition],
                provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions()));
    }

    [Fact]
    public async Task Explicit_bindings_do_not_fallback_to_another_variable_of_the_same_type()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<CapabilityDataException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.capability-json",
                [ExecutionDatum.Of("wrongName", new DemoInput("same CLR type"))],
                scope.ServiceProvider));
    }

    [Fact]
    public void Data_names_are_unique_with_polymorphic_reads_and_unconditional_replacement()
    {
        var data = new DataResolver().CreateScope(
            [ExecutionDatum.Of("customer", new DemoInput("old"))]);
        Assert.IsType<DemoInput>(data.Resolve(typeof(object), "customer"));
        data.Store("customer", typeof(DemoInput), new DemoInput("new"));
        Assert.Equal("new", Assert.IsType<DemoInput>(
            data.Resolve(typeof(DemoInput), "customer")).Value);
        Assert.Throws<CapabilityDataException>(() =>
            data.Store("customer", typeof(string), "incompatible"));
    }

    [Fact]
    public async Task Global_timeout_cancels_and_preserves_a_timeout_breadcrumb()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"slow"}""");
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<CapabilityJsonDispatcher>().ExecuteAsync(
                "demo.capability-json-timeout", payload.RootElement, scope.ServiceProvider));
        var trail = Assert.IsAssignableFrom<IReadOnlyList<ExecutionBreadcrumb>>(
            error.Data["Arcanum.Breadcrumbs"]);
        Assert.Contains(trail, b => b.Kind == "Timeout");
        Assert.DoesNotContain(trail, b => b.Kind == "CapabilityCompleted");
    }

    [Fact]
    public void Input_model_and_response_type_are_checked_against_descriptors()
    {
        using var provider = Provider();
        var definition = Demo();
        definition.Templates["echo"].Outputs["result"] = "MissingModel";
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition],
                provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions()));
    }
}
