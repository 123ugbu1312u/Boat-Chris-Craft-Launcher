using System;

namespace BCCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record CurseforgeLinks(
    string websiteUrl,
    string wikiUrl,
    string issuesUrl,
    string sourceUrl);