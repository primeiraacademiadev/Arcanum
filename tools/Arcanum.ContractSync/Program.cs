using System.Reflection;
using Arcanum.Framework;

if (args.Length != 4 || !args.Contains("--assembly") || !args.Contains("--input"))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/Arcanum.ContractSync -- --assembly <trusted-processes.dll> --input <capability.json>");
    return 1;
}

try
{
    var assemblyPath = Path.GetFullPath(args[Array.IndexOf(args, "--assembly") + 1]);
    var inputPath = Path.GetFullPath(args[Array.IndexOf(args, "--input") + 1]);
    var assembly = Assembly.LoadFrom(assemblyPath);
    var original = File.ReadAllText(inputPath);
    var definition = CapabilityCatalog.ParseJson(original);
    if (definition.Templates.Count == 0)
        throw new CapabilityDefinitionException("Contract synchronization supports Capability JSON vNext templates only.");

    // Only Process types explicitly declared by templates in the specified
    // trusted assembly are eligible. No broad assembly scan.
    var types = definition.Templates.Values.Select(t =>
        assembly.GetType(t.Process, throwOnError: true)
        ?? throw new CapabilityDefinitionException($"Unknown Process '{t.Process}'.")).ToArray();
    var report = new CapabilityContractSynchronizer(new ProcessCatalog(types)).Analyze(original);
    foreach (var difference in report.Differences)
        Console.Error.WriteLine(difference);
    Console.Out.WriteLine(report.ProposedJson);
    return report.HasDrift ? 2 : 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
