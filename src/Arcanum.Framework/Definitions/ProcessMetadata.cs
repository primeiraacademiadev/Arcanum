using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class ProcessMetadata
{
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? SaveResultAs { get; set; }
}
