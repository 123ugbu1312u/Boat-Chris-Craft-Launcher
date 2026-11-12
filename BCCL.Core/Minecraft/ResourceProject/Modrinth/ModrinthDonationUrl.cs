using System;

namespace BCCL.Core.Minecraft.ResourceProject.Modrinth;

[Serializable]
public record ModrinthDonationUrl(
    string id,
    string platform,
    string url);