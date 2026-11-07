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

        var types = processAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IProcess).IsAssignableFrom(t))
            .Distinct()
            .ToArray();

        var catalog = new ProcessCatalog(types);
        services.AddSingleton(catalog);
        foreach (var type in types)
            services.AddTransient(type);

        services.AddOptions<CapabilityOptions>();
        services.AddSingleton<ProcessResolver>();
        services.AddSingleton<DataResolver>();
        services.AddSingleton<CapabilityResolver>();
        return services;
    }

    public static IServiceCollection AddArcanumCapabilitiesFromDirectory(
        this IServiceCollection services, string directory)
    {
        services.AddSingleton(sp => CapabilityCatalog.LoadFromDirectory(
            directory,
            sp.GetRequiredService<ProcessCatalog>(),
            sp.GetRequiredService<IOptions<CapabilityOptions>>().Value));
        return services;
    }
}
