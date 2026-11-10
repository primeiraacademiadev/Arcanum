using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanum.Framework;

public sealed record ArgumentBinding(Type ParameterType, string ParameterName, string? DataName, bool IsCancellationToken);
