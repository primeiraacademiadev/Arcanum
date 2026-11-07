using System.Text.Json.Serialization;

namespace Arcanum.Framework;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromCapabilityDataAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
