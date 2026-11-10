using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed record ProcessResult<T>(T? Data, string Intention) : IProcessResult
{
    object? IProcessResult.Data => Data;
    Type IProcessResult.DataType => typeof(T);
}
