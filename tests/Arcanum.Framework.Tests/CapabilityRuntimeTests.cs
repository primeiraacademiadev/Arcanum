using Arcanum.Demo.Api;
using Arcanum.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Arcanum.Framework.Tests;

public class CapabilityRuntimeTests
{
    private static ServiceProvider CreateProvider(int defaultMaxSteps = 100)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExecutionProbe, ExecutionProbe>();
        services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
        services.Configure<CapabilityOptions>(options => options.DefaultMaxSteps = defaultMaxSteps);
        services.AddArcanumCapabilitiesFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "capabilities"));

        var provider = services.BuildServiceProvider(validateScopes: true);
        _ = provider.GetRequiredService<CapabilityCatalog>();
        return provider;
    }

    private static ExecutionDatum[] Data(DemoInput request) =>
    [
        ExecutionDatum.Of("request", request),
        ExecutionDatum.Of("counter", new CounterState(0))
    ];

    [Fact]
    public async Task Valid_pipeline_uses_sync_async_and_one_scoped_resource()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var resolver = provider.GetRequiredService<CapabilityResolver>();

        var result = await resolver.ExecuteAsync(
            "demo.publish", Data(new DemoInput("  abc ")), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("finished", result.Intention);
        Assert.Equal(3, result.Steps);
        var completed = Assert.IsType<CompletedData>(result.Data);
        Assert.Equal("ABC", completed.Text);
        Assert.True(completed.SameScopedResource);
    }

    [Fact]
    public async Task Invalid_input_stops_after_validation()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.publish", Data(new DemoInput("  ")), scope.ServiceProvider);

        Assert.Equal("@stop", result.Terminal);
        Assert.Equal("invalid", result.Intention);
        Assert.Equal(1, result.Steps);
        Assert.IsType<ValidationData>(result.Data);
    }

    [Fact]
    public async Task Same_stateless_process_can_be_visited_repeatedly()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 3)), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(3, result.Steps);
        Assert.Equal(3, Assert.IsType<CounterState>(result.Data).Count);
    }

    [Fact]
    public async Task Completion_at_exact_override_limit_is_allowed()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 4)), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(4, result.Steps);
    }

    [Fact]
    public async Task Override_limit_throws_before_fifth_execution()
    {
        using var provider = CreateProvider(defaultMaxSteps: 25);
        using var scope = provider.CreateScope();
        var exception = await Assert.ThrowsAsync<CapabilityExecutionLimitExceededException>(
            () => provider.GetRequiredService<CapabilityResolver>()
                .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 100)), scope.ServiceProvider));

        Assert.Equal(4, exception.Limit);
        Assert.Equal("demo.loop", exception.Capability);
    }

    [Fact]
    public async Task Framework_default_applies_when_capability_does_not_override()
    {
        using var provider = CreateProvider(defaultMaxSteps: 2);
        using var scope = provider.CreateScope();
        var exception = await Assert.ThrowsAsync<CapabilityExecutionLimitExceededException>(
            () => provider.GetRequiredService<CapabilityResolver>()
                .ExecuteAsync("demo.default-loop", Data(new DemoInput("ok", Iterations: 100)), scope.ServiceProvider));

        Assert.Equal(2, exception.Limit);
    }

    [Fact]
    public async Task Concurrent_invocations_do_not_share_execution_data_or_scoped_resources()
    {
        using var provider = CreateProvider();
        var resolver = provider.GetRequiredService<CapabilityResolver>();

        async Task<CapabilityExecutionResult> Run(string value)
        {
            using var scope = provider.CreateScope();
            return await resolver.ExecuteAsync(
                "demo.publish", Data(new DemoInput(value)), scope.ServiceProvider);
        }

        var outputs = await Task.WhenAll(Run("first"), Run("second"));
        var one = Assert.IsType<CompletedData>(outputs[0].Data);
        var two = Assert.IsType<CompletedData>(outputs[1].Data);

        Assert.Equal("FIRST", one.Text);
        Assert.Equal("SECOND", two.Text);
        Assert.True(one.SameScopedResource && two.SameScopedResource);
        Assert.NotEqual(one.ScopeId, two.ScopeId);
    }

    [Fact]
    public async Task Cancellation_is_honoured_before_invoking_a_process()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.publish", Data(new DemoInput("ok")), scope.ServiceProvider, source.Token));
    }

    [Fact]
    public async Task Missing_execution_data_fails_without_constructing_a_second_container()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<CapabilityDataException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.publish", Array.Empty<ExecutionDatum>(), scope.ServiceProvider));
    }

    [Fact]
    public void Data_resolution_by_type_rejects_ambiguity_and_allows_explicit_keys()
    {
        var data = new DataResolver().CreateScope(
        [
            ExecutionDatum.Of("a", new CounterState(1)),
            ExecutionDatum.Of("b", new CounterState(2))
        ]);

        Assert.Throws<CapabilityDataException>(() => data.Resolve(typeof(CounterState), null));
        Assert.Equal(1, Assert.IsType<CounterState>(data.Resolve(typeof(CounterState), "a")).Count);
    }

    [Fact]
    public void Unknown_graph_target_is_rejected_during_catalog_validation()
    {
        using var provider = CreateProvider();
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.publish.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var validate = typeof(ValidateProcess).FullName!;
        definition.Graph.Transitions[validate]["valid"] = "Some.Nonexistent.Process";

        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], provider.GetRequiredService<ProcessCatalog>(),
                new CapabilityOptions()));
    }

    [Fact]
    public async Task Unmapped_intention_raises_a_transition_exception()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.publish.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var validate = typeof(ValidateProcess).FullName!;
        definition.Graph.Transitions[validate].Remove("valid");

        var catalog = new CapabilityCatalog([definition],
            provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions());
        var resolver = new CapabilityResolver(
            catalog,
            provider.GetRequiredService<ProcessResolver>(),
            provider.GetRequiredService<DataResolver>(),
            Options.Create(new CapabilityOptions()),
            NullLogger<CapabilityResolver>.Instance);

        await Assert.ThrowsAsync<CapabilityTransitionException>(() =>
            resolver.ExecuteAsync(
                "demo.publish", Data(new DemoInput("ok")), scope.ServiceProvider));
    }

    [Fact]
    public void Nonpositive_default_and_override_are_rejected()
    {
        using var provider = CreateProvider();
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.loop.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var processes = provider.GetRequiredService<ProcessCatalog>();

        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions { DefaultMaxSteps = 0 }));
        definition.MaxSteps = 0;
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions()));
    }
}
