using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.IO.Net.Http;

/// <summary>Coalesced, short lived source selection for public vanilla game files.</summary>
public sealed class MinecraftDownloadSourceSelector(Func<string, CancellationToken, Task> probe)
{
    private sealed class Entry(Func<Task<string?>> select)
    {
        public long Expires = Environment.TickCount64 + 180000;
        public readonly Lazy<Task<string?>> Selected = new(select, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<IReadOnlyList<string>> OrderAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = GetGroup(urls);
        if (key is null) return urls;
        if (_entries.TryGetValue(key, out var expired) && expired.Expires <= Environment.TickCount64)
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, expired));
        var entry = _entries.GetOrAdd(key, _ => new Entry(() => SelectAsync(urls)));
        var selected = await entry.Selected.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (selected is null) entry.Expires = Environment.TickCount64 + 5000;
        return urls.OrderBy(url => Identity(url) == selected ? 0 : 1).ToArray();
    }

    public void ReportFailure(IReadOnlyList<string> urls, string failedUrl)
    {
        var key = GetGroup(urls);
        if (key is null || !_entries.TryGetValue(key, out var entry)) return;
        if (entry.Selected.IsValueCreated && entry.Selected.Value.IsCompletedSuccessfully &&
            entry.Selected.Value.Result == Identity(failedUrl))
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
    }

    private async Task<string?> SelectAsync(IReadOnlyList<string> urls)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        async Task<string?> TryAsync(string url)
        {
            try { await probe(url, budget.Token).ConfigureAwait(false); return Identity(url); }
            catch { return null; }
        }
        // Each attempt catches its own error, including cancellation after another source wins.
        var attempts = urls.Select(TryAsync).ToList();
        try
        {
            while (attempts.Count > 0)
            {
                var completed = await Task.WhenAny(attempts).ConfigureAwait(false);
                attempts.Remove(completed);
                var result = await completed.ConfigureAwait(false);
                if (result is not null) return result;
            }
            return null;
        }
        finally { budget.Cancel(); }
    }

    private static string Identity(string url)
    {
        var uri = new Uri(url);
        return uri.Host.Equals("bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)
            ? uri.Host + "/" + uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            : uri.Host;
    }

    private static string? GetGroup(IReadOnlyList<string> urls)
    {
        if (urls.Count < 2) return null;
        string? category = null;
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return null;
            var current = uri.IdnHost.ToLowerInvariant() switch
            {
                "resources.download.minecraft.net" => "assets",
                "libraries.minecraft.net" => "libraries",
                "piston-data.mojang.com" or "launcher.mojang.com" =>
                    uri.AbsolutePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? "client" : "metadata",
                "piston-meta.mojang.com" or "launchermeta.mojang.com" => "metadata",
                "bmclapi2.bangbang93.com" => null,
                _ => "unsupported"
            };
            if (current == "unsupported") return null;
            if (current is not null) category = current;
        }
        return category is null ? null : category + "|" + string.Join("|", urls.Select(Identity).Distinct().Order());
    }
}
