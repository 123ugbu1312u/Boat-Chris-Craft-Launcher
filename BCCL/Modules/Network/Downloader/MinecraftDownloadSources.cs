using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using BCCL.Core.IO.Net.Http;

namespace BCCL.Network;

internal static class MinecraftDownloadSources
{
    internal static readonly MinecraftDownloadSourceSelector Selector = new(ProbeAsync);

    private static async Task ProbeAsync(string url, CancellationToken cancellationToken)
    {
        const int sampleSize = 64 * 1024;
        var timer = Stopwatch.StartNew();
        using var lease = await DownloadResourceManager.AcquireConnectionAsync(url, cancellationToken).ConfigureAwait(false);
        using var request = FileDownloader.CreateDownloadRequest(url, false, "", DownloadRequestKind.SmallOrBatch);
        request.Headers.Range = new RangeHeaderValue(0, sampleSize - 1);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var response = await FileDownloader.SendDownloadRequestAsync(url, request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType == "text/html")
            throw new IOException("Download source probe failed.");
        if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.From != 0)
            throw new IOException("Invalid probe range.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = await DownloadResourceManager.RentBufferAsync(sampleSize, cancellationToken).ConfigureAwait(false);
        var received = 0;
        while (received < sampleSize)
        {
            var count = await stream.ReadAsync(buffer.Buffer.AsMemory(received, sampleSize - received), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            await DownloadResourceManager.ThrottleAsync(count, cancellationToken).ConfigureAwait(false);
            DownloadResourceManager.RecordDownloadedBytes(count);
            received += count;
        }
        if (received == 0) throw new IOException("Empty download source probe.");
        ModBase.Log($"[Download] 原版源采样 {timer.ElapsedMilliseconds} ms / {received} bytes：{url}");
    }
}
