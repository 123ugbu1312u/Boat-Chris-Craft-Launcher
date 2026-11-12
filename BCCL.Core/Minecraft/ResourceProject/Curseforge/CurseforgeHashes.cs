using System;

namespace BCCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeHashes(
    string value,
    int algo);