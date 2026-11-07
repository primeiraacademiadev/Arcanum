using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed record ExecutionDatum(string Name, Type DataType, object? Value)
{
    public static ExecutionDatum Of<T>(string name, T value) => new(name, typeof(T), value);
}
