namespace Arcanum.Framework;

public sealed class CapabilityGraph
{
    public string Entry { get; set; } = "";

    // Legacy single-target syntax remains valid for existing Capabilities.
    public Dictionary<string, Dictionary<string, string>> Transitions { get; set; } =
        new(StringComparer.Ordinal);

    // New syntax allows several edges for the same source and intention.
    public List<CapabilityEdge> Edges { get; set; } = [];

    public IEnumerable<CapabilityEdge> AllEdges =>
        Edges.Count > 0
            ? Edges
            : Transitions.SelectMany(source => source.Value.Select(pair => new CapabilityEdge
            {
                From = source.Key,
                Intention = pair.Key,
                To = pair.Value
            }));

    public CapabilityEdge[] Matching(string from, string intention) =>
        AllEdges.Where(edge => edge.From == from && edge.Intention == intention).ToArray();

    public int IncomingCount(string node) => AllEdges.Count(edge => edge.To == node);

    // Conservative while a Process is running: all its declared intentions are possible.
    // Once it completes, the scheduler replaces that uncertainty with the selected edges.
    public bool CanReach(string source, string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
                continue;
            if (current == target)
                return true;
            foreach (var edge in AllEdges.Where(edge => edge.From == current && !edge.To.StartsWith('@')))
                queue.Enqueue(edge.To);
        }
        return false;
    }
}
