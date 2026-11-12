using System.Text.Json.Serialization;

namespace BCCL.Core.Link.McPing.Model;

public record McPingPlayerSampleResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] string Id);