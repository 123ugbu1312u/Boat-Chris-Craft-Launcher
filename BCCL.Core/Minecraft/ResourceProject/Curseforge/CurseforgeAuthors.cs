using System;

namespace BCCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeAuthors(
    int id,
    string name,
    string url);