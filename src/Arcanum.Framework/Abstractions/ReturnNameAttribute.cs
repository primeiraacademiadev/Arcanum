namespace Arcanum.Framework;

/// <summary>
/// Assigns the name used by the DataResolver to store the typed result of Execute/ExecuteAsync.
/// A Capability can override this name using saveResultAs.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReturnNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
