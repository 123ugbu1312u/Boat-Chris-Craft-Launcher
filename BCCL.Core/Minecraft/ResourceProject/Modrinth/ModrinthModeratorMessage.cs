using System;

namespace BCCL.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthModeratorMessage(
    string message,
    string? body);