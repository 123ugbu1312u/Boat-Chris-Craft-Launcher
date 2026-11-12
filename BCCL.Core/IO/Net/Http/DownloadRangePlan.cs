using System;
using System.Collections.Generic;

namespace BCCL.Core.IO.Net.Http;

public static class DownloadRangePlan
{
    /// <summary>Enough independent ranges to use configured workers, without tiny requests or unbounded fragmentation.</summary>
    public static IReadOnlyList<(long Start, long End)> Create(long size, int connections)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        const long minSegment = 2L * 1024 * 1024;
        const long targetSegment = 8L * 1024 * 1024;
        var baseline = size / targetSegment + (size % targetSegment == 0 ? 0 : 1);
        var concurrent = Math.Min(Math.Clamp(connections, 1, 8), Math.Max(1, size / minSegment));
        var count = Math.Clamp(Math.Max(baseline, concurrent), 1, 1024);
        var segmentSize = size / count + (size % count == 0 ? 0 : 1);
        var ranges = new List<(long, long)>();
        for (long start = 0; start < size;)
        {
            var length = Math.Min(segmentSize, size - start);
            ranges.Add((start, start + length - 1));
            start += length;
        }
        return ranges;
    }
}
