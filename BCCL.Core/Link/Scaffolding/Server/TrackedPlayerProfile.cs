using BCCL.Core.Link.Scaffolding.Client.Models;
using System;

namespace BCCL.Core.Link.Scaffolding.Server;

public record TrackedPlayerProfile
{
    public required PlayerProfile Profile { get; set; }
    public required DateTime LastSeenUtc { get; set; }
};