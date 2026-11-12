using System;
using System.Collections.Generic;

namespace BCCL.Core.Minecraft.ResourceProject.Curseforge;

[Serializable]
public record class CurseforgeProjectResponse(CurseforgeProject data);
[Serializable]
public record class CurseforgeProjectsResponse(List<CurseforgeProject> data);
