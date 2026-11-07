namespace Arcanum.Framework;

/// <summary>
/// Explicit marker for a class that may be discovered and executed as an Arcanum Process.
/// Execute/ExecuteAsync signatures are validated independently to allow typed method arguments.
/// </summary>
public interface IProcess { }
