using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public class CapabilityDataException(string message) : InvalidOperationException(message) { }
