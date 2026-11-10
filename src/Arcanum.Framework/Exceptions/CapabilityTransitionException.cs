using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public class CapabilityTransitionException(string message) : InvalidOperationException(message) { }
