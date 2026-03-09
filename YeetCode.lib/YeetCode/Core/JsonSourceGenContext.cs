namespace YeetCode.Core;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON serialization context for primitive types.
/// Required by .NET 10 which disables reflection-based serialization by default.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
public partial class YeetCodeJsonContext : JsonSerializerContext { }