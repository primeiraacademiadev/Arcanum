using System.Text.Json;

namespace Arcanum.Framework;

public sealed class CapabilityInputContract
{
    /// <summary>Full CLR type name, restricted to Execute argument types of trusted Processes.</summary>
    public string Type { get; set; } = "";
    public string? Model { get; set; }

    /// <summary>JSON source: $ for the body, or $.property.subproperty for a nested value.</summary>
    public string? Source { get; set; }

    /// <summary>Constant JSON value used to seed execution data instead of reading the request.</summary>
    public JsonElement Default { get; set; }
}
