using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcanum.Framework;

public static class ArcanumServiceCollectionExtensions
{
    public static IServiceCollection AddArcanumFramework(
        this IServiceCollection services, params Assembly[] processAssemblies)
    {
        if (processAssemblies.Length == 0)
            throw new ArgumentException("Specify at least one Process assembly.", nameof(processAssemblies));

        // The JSON declarations determine which CLR classes are executable.
        // Assemblies are a trusted allowlist, not an instruction to scan for Processes.
        services.AddSingleton(new ProcessAssemblyRegistry(processAssemblies));

        services.AddOptions<CapabilityOptions>();
        services.AddSingleton<ProcessResolver>();
        services.AddSingleton<DataResolver>();
        services.AddSingleton<CapabilityResolver>();
        services.AddSingleton<CapabilityContractResolver>();
        services.AddSingleton<CapabilityJsonDispatcher>();
        return services;
    }

    public static IServiceCollection AddArcanumCapabilitiesFromDirectory(
        this IServiceCollection services, string directory)
    {
        var registry = services
            .LastOrDefault(x => x.ServiceType == typeof(ProcessAssemblyRegistry))?.ImplementationInstance
            as ProcessAssemblyRegistry
            ?? throw new InvalidOperationException(
                "Register trusted assemblies with AddArcanumFramework first.");

        if (!Directory.Exists(directory))
            throw new CapabilityDefinitionException($"Capability directory does not exist: {directory}");

        var definitions = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CapabilityCatalog.ParseJson(File.ReadAllText(path)))
            .ToArray();

        var types = definitions
            .SelectMany(definition => definition.Processes.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(registry.Resolve)
            .ToArray();

        var catalog = new ProcessCatalog(types);
        services.AddSingleton(catalog);
        foreach (var type in types)
            services.AddTransient(type);

        services.AddSingleton(sp => new CapabilityCatalog(
            definitions, catalog,
            sp.GetRequiredService<IOptions<CapabilityOptions>>().Value));
        return services;
    }
}
