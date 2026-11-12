using System.Text.Json.Serialization;

// ReSharper disable InconsistentNaming

namespace BCCL.Core.Link.Scaffolding.Client.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayerKind
{
    HOST,
    GUEST
}