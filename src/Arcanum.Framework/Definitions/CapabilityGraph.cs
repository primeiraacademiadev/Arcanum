using System.Text.Json.Serialization;

namespace Arcanum.Framework;

// Nodes have graph-local identities. Edges are declared separately from node metadata.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CapabilityGraph
{
    public string Entry { get; set; } = "";
    public Dictionary<string, CapabilityNode> Nodes { get; set; } = new(StringComparer.Ordinal);
    public List<CapabilityEdge> Edges { get; set; } = [];

    // One intention selects at most one conditional edge. Parallel edges run
    // independently of the ProcessResult intention (and may be several).
    public CapabilityEdge[] Matching(string from, string intention) =>
        Edges.Where(edge => edge.From == from && (edge.Parallel || edge.Intention == intention)).ToArray();

    public int IncomingCount(string node) => Edges.Count(edge => edge.To == node);

    // During execution all future outcomes are possible until a Process completes.
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
            foreach (var edge in Edges.Where(edge => edge.From == current && !edge.To.StartsWith('@')))
                queue.Enqueue(edge.To);
        }
        return false;
    }
}
