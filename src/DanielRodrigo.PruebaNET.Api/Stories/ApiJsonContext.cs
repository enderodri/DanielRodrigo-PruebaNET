using System.Text.Json.Serialization;

namespace DanielRodrigo.PruebaNET.Api.Stories;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoryResponse[]))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
