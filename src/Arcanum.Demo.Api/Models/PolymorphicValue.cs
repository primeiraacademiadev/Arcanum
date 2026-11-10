namespace Arcanum.Demo.Api;

public interface IPolymorphicValue
{
    string Kind { get; }
}
public sealed record CustomerValue(string Kind) : IPolymorphicValue;
public sealed record SupplierValue(string Kind) : IPolymorphicValue;
