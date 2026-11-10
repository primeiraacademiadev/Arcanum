namespace Arcanum.Framework;

/// <summary>
/// Optional documentation-only annotation. Graph JSON determines which classes are executable;
/// this attribute has no effect on registration or resolution.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProcessAttribute : Attribute { }
