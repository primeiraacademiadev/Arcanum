using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityOptions
{
    public int DefaultMaxSteps { get; set; } = 100;
    public int DefaultMaxDepth { get; set; } = 100;
    // Applied to the whole Capability execution, not separately to each Node.
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxTraceSnapshotBytes { get; set; } = 64 * 1024;
    public string[] TraceRedactedPropertyNames { get; set; } =
        ["password", "secret", "token", "authorization", "apiKey", "accessToken", "refreshToken"];
}
