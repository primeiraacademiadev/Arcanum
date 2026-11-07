using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed record DemoInput(string? Value, int Iterations = 1);
