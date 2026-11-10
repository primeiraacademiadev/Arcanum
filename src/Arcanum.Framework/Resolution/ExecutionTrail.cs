using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Arcanum.Framework;

// Execution-local, append-only and thread-safe. No mutable business objects are retained.
internal sealed class ExecutionTrail(CapabilityOptions options, bool traceEnabled, ILogger logger)
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<ExecutionBreadcrumb> _events = [];
    private long _sequence;

    public void Add(
        string kind,
        string? nodeId = null,
        Guid? nodeExecutionId = null,
        string? method = null,
        string? intention = null,
        string? relatedNodeId = null,
        double? durationMs = null,
        string? detail = null,
        object? snapshot = null,
        bool captureSnapshot = false,
        Guid? causedByNodeExecutionId = null)
    {
        var (json, truncated) = traceEnabled && captureSnapshot
            ? Capture(snapshot)
            : (null, false);
        lock (_gate)
        {
            _events.Add(new ExecutionBreadcrumb(
                ++_sequence,
                DateTimeOffset.UtcNow,
                _clock.Elapsed.TotalMilliseconds,
                Environment.CurrentManagedThreadId,
                kind,
                nodeId,
                nodeExecutionId,
                method,
                intention,
                relatedNodeId,
                durationMs,
                detail,
                json,
                truncated,
                causedByNodeExecutionId));
        }
    }

    public IReadOnlyList<ExecutionBreadcrumb> Snapshot()
    {
        lock (_gate)
            return _events.ToArray();
    }

    private (string? Json, bool Truncated) Capture(object? value)
    {
        try
        {
            var node = JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Redact(node);
            var json = node?.ToJsonString() ?? "null";
            // Do not expose a partial JSON document or the potentially sensitive suffix.
            return Encoding.UTF8.GetByteCount(json) > options.MaxTraceSnapshotBytes
                ? (null, true)
                : (json, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Snapshot failures are non-fatal but still exceptions: never hide
            // them from the host's structured logger.
            logger.LogError(exception, "Arcanum Trace snapshot serialization failed");
            return ($"\"[snapshot unavailable: {exception.GetType().Name}]\"", false);
        }
    }

    private void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (options.TraceRedactedPropertyNames.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
                    obj[property.Key] = "[redacted]";
                else
                    Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                Redact(item);
        }
    }
}
