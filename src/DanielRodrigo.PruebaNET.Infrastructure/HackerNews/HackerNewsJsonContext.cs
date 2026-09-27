using System.Text.Json.Serialization;

namespace DanielRodrigo.PruebaNET.Infrastructure.HackerNews;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HackerNewsItem))]
[JsonSerializable(typeof(long[]))]
internal sealed partial class HackerNewsJsonContext : JsonSerializerContext;
