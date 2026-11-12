using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading.Channels;
using BCCL.Core.Link.MCTier;

namespace BCCL.Multiplayer;

/// <summary>Paced capture, one frame sending and one replaceable latest frame; never a growing video queue.</summary>
internal sealed class RoomScreenStream
{
    public const int TargetFramesPerSecond = 60;
    private readonly CancellationTokenSource stop = new();
    private long framesSent;
    public long FramesSent => Interlocked.Read(ref framesSent);
    public Task Completion { get; }

    public RoomScreenStream(Func<(Func<ExtrasPacket> Capture, Action Release)> prepare,
        Func<bool> hasPeers, Func<ExtrasPacket, Task> send, Action<Exception> failed)
    {
        var frames = Channel.CreateBounded<ExtrasPacket>(new BoundedChannelOptions(1)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
        var sending = Task.Run(async () =>
        {
            try
            {
                while (await frames.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
                {
                    if (stop.IsCancellationRequested) break;
                    if (frames.Reader.TryRead(out var frame))
                    { await send(frame).ConfigureAwait(false); Interlocked.Increment(ref framesSent); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex) { failed(ex); Stop(); }
        });
        var capturing = Task.Factory.StartNew(() =>
        {
            Action? release = null;
            try
            {
                var source = prepare(); release = source.Release;
                using var timer = CreateTimer();
                WaitHandle[]? waits = timer is null ? null : [stop.Token.WaitHandle, timer];
                var interval = Stopwatch.Frequency / (double)TargetFramesPerSecond;
                double next = Stopwatch.GetTimestamp();
                while (!stop.IsCancellationRequested)
                {
                    if (!hasPeers())
                    { stop.Token.WaitHandle.WaitOne(100); next = Stopwatch.GetTimestamp(); continue; }
                    while (!stop.IsCancellationRequested)
                    {
                        var remaining = (next - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                        if (remaining <= 0) break;
                        if (remaining > 1.5 && timer is not null)
                        {
                            var due = -(long)((remaining - 0.5) * 10000);
                            if (SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, 0, 0, false)) WaitHandle.WaitAny(waits!);
                            else stop.Token.WaitHandle.WaitOne(Math.Max(1, (int)remaining - 1));
                        }
                        else if (remaining > 1.5) stop.Token.WaitHandle.WaitOne(Math.Max(1, (int)remaining - 1));
                        else Thread.SpinWait(100);
                    }
                    if (stop.IsCancellationRequested) break;
                    var frame = source.Capture();
                    if (stop.IsCancellationRequested) break;
                    frames.Writer.TryWrite(frame);
                    next = Math.Max(next + interval, Stopwatch.GetTimestamp());
                }
            }
            catch (Exception ex) { if (!stop.IsCancellationRequested) { failed(ex); Stop(); } }
            finally { try { release?.Invoke(); } finally { frames.Writer.TryComplete(); } }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Completion = FinishAsync(capturing, sending);
    }
    private async Task FinishAsync(Task capturing, Task sending)
    { try { await Task.WhenAll(capturing, sending).ConfigureAwait(false); } finally { stop.Dispose(); } }
    public void Stop() { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
    private static TimerHandle? CreateTimer()
    {
        var handle = CreateWaitableTimerExW(0, 0, 2, 0x00100002);
        return handle == 0 ? null : new TimerHandle(handle);
    }
    private sealed class TimerHandle : WaitHandle
    { public TimerHandle(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, true); }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);
    [DllImport("kernel32.dll")] private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, nint callback, nint argument, bool resume);
}
