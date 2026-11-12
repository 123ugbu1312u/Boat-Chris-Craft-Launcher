using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BCCL.Core.IO.Net.Http;

/// <summary>Race a preferred read against a delayed fallback without leaving losing requests running.</summary>
public static class HedgedResourceRequest
{
    public static async Task<T> FetchAsync<T>(Func<CancellationToken, Task<T>> preferred,
        Func<CancellationToken, Task<T>> fallback, TimeSpan delay, CancellationToken cancellationToken)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        async Task<(T? Value, Exception? Error)> TryAsync(Func<CancellationToken, Task<T>> fetch)
        {
            try { return (await fetch(race.Token).ConfigureAwait(false), null); }
            catch (Exception ex) { return (default, ex); }
        }

        var first = TryAsync(preferred);
        try
        {
            await Task.WhenAny(first, Task.Delay(delay, race.Token)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (first.IsCompletedSuccessfully && first.Result.Error is null) return first.Result.Value!;
            var pending = new List<Task<(T? Value, Exception? Error)>> { first, TryAsync(fallback) };
            var errors = new List<Exception>();
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
                pending.Remove(finished);
                var result = await finished.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Error is null) return result.Value!;
                errors.Add(result.Error);
            }
            throw new AggregateException("All resource list endpoints failed.", errors);
        }
        finally { race.Cancel(); }
    }
}

/// <summary>
/// Holds one page per source. Only the caller consumes results; pending sources never mutate a displayed list.
/// A slow source is retained for the next page rather than blocking a useful response from another source.
/// </summary>
public sealed class ProgressiveResourceSources<T>
{
    public sealed record Result(string Source, T? Value, Exception? Error);
    private readonly Dictionary<string, Task<Result>> _pending = new();
    public bool HasPending => _pending.Count > 0;

    public void Start(string source, Func<Task<T>> fetch)
    {
        if (_pending.ContainsKey(source)) return;
        async Task<Result> CaptureAsync()
        {
            try { return new(source, await fetch().ConfigureAwait(false), null); }
            catch (Exception ex) { return new(source, default, ex); }
        }
        _pending.Add(source, CaptureAsync());
    }

    public async Task<IReadOnlyList<Result>> TakeReadyAsync(Func<T, bool> hasResults, TimeSpan grace,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Result>();
        while (_pending.Count > 0)
        {
            await Task.WhenAny(_pending.Values).WaitAsync(cancellationToken).ConfigureAwait(false);
            await DrainAsync().ConfigureAwait(false);
            if (!results.Any(r => r.Error is null && hasResults(r.Value!))) continue;
            if (_pending.Count > 0)
            {
                // Give a similarly fast source a small chance to join, bounded by one shared delay.
                var all = Task.WhenAll(_pending.Values);
                await Task.WhenAny(all, Task.Delay(grace, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await DrainAsync().ConfigureAwait(false);
            }
            break;
        }
        return results;

        async Task DrainAsync()
        {
            foreach (var pair in _pending.Where(p => p.Value.IsCompleted).ToArray())
            {
                results.Add(await pair.Value.ConfigureAwait(false));
                _pending.Remove(pair.Key);
            }
        }
    }
}
