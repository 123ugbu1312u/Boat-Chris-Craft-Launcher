using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BCCL.Core.Link.McPing.Model;

public record McPingPlayerResult(
    [property: JsonPropertyName("max")] int Max,
    [property: JsonPropertyName("online"), JsonRequired] int Online,
    [property: JsonPropertyName("sample")] List<McPingPlayerSampleResult>? Samples);
