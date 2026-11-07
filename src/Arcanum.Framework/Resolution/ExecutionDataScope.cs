namespace Arcanum.Framework;

public sealed class ExecutionDataScope
{
    private readonly record struct Entry(Type Type, object? Value);

    private readonly Dictionary<string, Entry> _data = new(StringComparer.Ordinal);

    public void Store(string name, Type type, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new CapabilityDataException("A data key cannot be blank.");
        if (value is not null && !type.IsInstanceOfType(value))
            throw new CapabilityDataException($"Value for '{name}' is not compatible with '{type.FullName}'.");
        if (_data.TryGetValue(name, out var previous) && previous.Type != type)
            throw new CapabilityDataException(
                $"The data key '{name}' was already used for '{previous.Type.FullName}'.");

        // A loop may revisit a stateless Process. Its latest typed result replaces the previous value.
        _data[name] = new Entry(type, value);
    }

    public object? Resolve(Type expectedType, string? name, string? parameterName = null)
    {
        if (name is not null)
        {
            if (!_data.TryGetValue(name, out var value))
                throw new CapabilityDataException($"Missing execution data '{name}'.");
            if (!expectedType.IsAssignableFrom(value.Type))
                throw new CapabilityDataException(
                    $"Data '{name}' has type '{value.Type.FullName}', not '{expectedType.FullName}'.");
            return value.Value;
        }


        // Parameter name is the conventional key. A matching but incompatible key is an error.
        if (parameterName is not null && _data.TryGetValue(parameterName, out var named))
        {
            if (!expectedType.IsAssignableFrom(named.Type))
                throw new CapabilityDataException(
                    $"Data '{parameterName}' has type '{named.Type.FullName}', not '{expectedType.FullName}'.");
            return named.Value;
        }

        var matches = _data.Values
            .Where(v => expectedType.IsAssignableFrom(v.Type)).ToArray();

        return matches.Length switch
        {
            1 => matches[0].Value,
            0 => throw new CapabilityDataException($"No execution data of type '{expectedType.FullName}'."),
            _ => throw new CapabilityDataException(
                $"Ambiguous data type '{expectedType.FullName}'; use a matching parameter name or FromCapabilityData.")
        };
    }
}
