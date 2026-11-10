using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public class CapabilityDefinitionException(string message) : InvalidOperationException(message) { }
