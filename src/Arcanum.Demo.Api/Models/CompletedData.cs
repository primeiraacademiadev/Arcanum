using Arcanum.Framework;

namespace Arcanum.Demo.Api;

public sealed record CompletedData(string Text, Guid ScopeId, bool SameScopedResource);
