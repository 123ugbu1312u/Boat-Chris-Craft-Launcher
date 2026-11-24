using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net;
using BCCL.Core.Logging;

namespace BCCL;

internal static class ServerIpRegion
{
    private readonly record struct Range(UInt128 First, UInt128 Last, bool IsOverseas);

    private static readonly Lazy<(Range[] IPv4, Range[] IPv6)> Ranges = new(LoadRanges);

    internal static bool? IsOverseas(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        var ipv4 = bytes.Length == 4;
        UInt128 value = ipv4 ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt128BigEndian(bytes);
        var ranges = ipv4 ? Ranges.Value.IPv4 : Ranges.Value.IPv6;
        var left = 0;
        var right = ranges.Length - 1;
        while (left <= right)
        {
            var middle = left + (right - left) / 2;
            var range = ranges[middle];
            if (value < range.First)
                right = middle - 1;
            else if (value > range.Last)
                left = middle + 1;
            else
                return range.IsOverseas;
        }
        return null;
    }

    private static (Range[], Range[]) LoadRanges()
    {
        try
        {
            using var resource = System.Windows.Application.GetResourceStream(new Uri(
                "pack://application:,,,/BCCL;component/Resources/ServerRegions/db-ip-regions.bin.gz"))!.Stream;
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            return (ReadRanges(), ReadRanges());

            UInt128 ReadAddress() => ((UInt128)reader.ReadUInt64() << 64) | reader.ReadUInt64();

            Range[] ReadRanges()
            {
                var ranges = new Range[reader.ReadInt32()];
                for (var index = 0; index < ranges.Length; index++)
                    ranges[index] = new Range(ReadAddress(), ReadAddress(), reader.ReadBoolean());
                return ranges;
            }
        }
        catch (Exception error)
        {
            LogWrapper.Debug(error, nameof(ServerIpRegion), "Failed to load packaged server regions");
            return ([], []);
        }
    }
}
