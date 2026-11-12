using System;
using System.Net;

namespace BCCL.Core.Link;

public record BroadcastRecord(string Desc, IPEndPoint Address, DateTime FoundAt);