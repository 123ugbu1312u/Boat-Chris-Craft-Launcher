using System.Text.Json.Serialization;

namespace BCCL.Core.Link.McPing.Model;

public record McPingVersionResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("protocol")] int Protocol);