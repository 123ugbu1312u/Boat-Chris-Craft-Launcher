using System;

namespace BCCL.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthLicense(
    string id,
    string name,
    string? url);