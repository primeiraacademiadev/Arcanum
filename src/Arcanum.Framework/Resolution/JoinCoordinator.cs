namespace Arcanum.Framework;

/// <summary>
/// Dynamic join readiness, based on remaining *possible* routes from both
/// running and scheduled Nodes; diverged branches continue independently.
/// </summary>
internal static class JoinCoordinator
{
    public static string[] ReadyNodes(
        CapabilityGraph graph,
        IEnumerable<string> queuedNodes,
        IEnumerable<string> runningNodes)
    {
        var queued = queuedNodes.ToArray();
        var running = runningNodes.ToArray();
        return queued.Where(candidate =>
            !running.Any(active => graph.CanReach(active, candidate)) &&
            !queued.Any(other => other != candidate && graph.CanReach(other, candidate)))
            .ToArray();
    }
}
