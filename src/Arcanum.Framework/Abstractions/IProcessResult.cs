using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public interface IProcessResult
{
    object? Data { get; }
    Type DataType { get; }
    string Intention { get; }
}
