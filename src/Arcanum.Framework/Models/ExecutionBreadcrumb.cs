namespace Arcanum.Framework;

// Sequence is the order in which the resolver OBSERVED events, not a distributed clock.
// ThreadId, invocation identity and elapsed time make overlapping methods diagnosable.
public sealed record ExecutionBreadcrumb(
    long Sequence,
    DateTimeOffset Timestamp,
    double ElapsedMs,
    int ThreadId,
    string Kind,
    string? NodeId = null,
    Guid? NodeExecutionId = null,
    string? Method = null,
    string? Intention = null,
    string? RelatedNodeId = null,
    double? DurationMs = null,
    string? Detail = null,
    string? SnapshotJson = null,
    bool SnapshotTruncated = false);
