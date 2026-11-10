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
    public async Task VNext_multiple_spawn_edges_converge_and_detached_work_is_drained()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":" AbC "}""");
        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>().ExecuteAsync(
            "demo.capability-json-parallel", payload.RootElement, scope.ServiceProvider);
        Assert.Equal("ABC|abc", Assert.IsType<string>(result.Data));
        Assert.Equal(6, result.Steps);
        Assert.Null(result.Intention); // detached leaf and join have independent intentions
        var join = Assert.Single(result.Breadcrumbs, b => b.NodeId == "join" && b.Kind == "Started");
        Assert.True(join.Sequence >
            Assert.Single(result.Breadcrumbs, b => b.NodeId == "alpha" && b.Kind == "Completed").Sequence);
        Assert.True(join.Sequence >
            Assert.Single(result.Breadcrumbs, b => b.NodeId == "beta" && b.Kind == "Completed").Sequence);
        Assert.True(Assert.Single(result.Breadcrumbs, b => b.Kind == "CapabilityCompleted").Sequence >
            Assert.Single(result.Breadcrumbs, b => b.NodeId == "gamma" && b.Kind == "Completed").Sequence);
        Assert.Equal(2, result.Breadcrumbs.Count(b =>
            b.Kind == "Transition" && b.NodeId == "fork" && b.Intention == "@spawn"));
    }

    [Fact]
    public async Task Interface_model_survives_replacement_by_another_concrete_Process_result()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"input"}""");

        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>().ExecuteAsync(
            "demo.capability-json-polymorphism", payload.RootElement, scope.ServiceProvider);
        Assert.Equal(3, result.Steps);
        Assert.Equal("supplier", Assert.IsType<string>(result.Data));
        Assert.Equal("summarized", result.Intention);
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
    public void Contract_sync_proposes_updated_JSON_without_changing_the_graph_or_description()
    {
        using var provider = Provider();
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "capabilities", "demo.capability-json.json"));
        var changed = original.Replace(
            "\"result\": \"DemoInput\"",
            "\"result\": \"MissingModel\"",
            StringComparison.Ordinal);

        var tool = new CapabilityContractSynchronizer(
            provider.GetRequiredService<ProcessCatalog>());
        var report = tool.Analyze(changed);
        Assert.True(report.HasDrift);
        Assert.Contains(report.Differences, x => x.Contains("ProcessResult<T>", StringComparison.Ordinal));
        Assert.Contains("The same EchoActivity template", report.ProposedJson);
        Assert.Contains("\"firstEcho\"", report.ProposedJson);
        var proposal = CapabilityCatalog.ParseJson(report.ProposedJson);
        _ = new CapabilityCatalog([proposal],
            provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions());
        Assert.False(tool.Analyze(report.ProposedJson).HasDrift);
    }

    [Fact]
    public void Synchronizer_preserves_intentional_interface_output_models()
    {
        using var provider = Provider();
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "capabilities", "demo.capability-json-polymorphism.json"));
        var report = new CapabilityContractSynchronizer(
            provider.GetRequiredService<ProcessCatalog>()).Analyze(original);
        Assert.False(report.HasDrift);
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
