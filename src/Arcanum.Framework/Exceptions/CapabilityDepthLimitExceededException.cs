namespace Arcanum.Framework;

public sealed class CapabilityDepthLimitExceededException : Exception
{
    public string Capability { get; }
    public int Version { get; }
    public string NodeId { get; }
    public int Limit { get; }

    public CapabilityDepthLimitExceededException(string capability, int version, string nodeId, int limit)
        : base($"Capability '{capability}' v{version} exceeded maxDepth {limit} before node '{nodeId}'.")
    {
        Capability = capability;
        Version = version;
        NodeId = nodeId;
        Limit = limit;
    }
}
