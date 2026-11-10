using System.Text.Json;

namespace Arcanum.Framework;

/// <summary>A named CLR contract; foreign/external references are metadata-only RFU.</summary>
public sealed class CapabilityModel
{
    public string Type { get; set; } = "";
    public string? Description { get; set; }
    public string? ForeignKey { get; set; }
    public JsonElement? ExternalKey { get; set; }
}
