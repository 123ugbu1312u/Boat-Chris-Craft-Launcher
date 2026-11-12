using NAudio.Wave;

namespace BCCL.Multiplayer;

/// <summary>Explicitly joined room voice. Capture and playback are independently controlled.</summary>
internal sealed class RoomAudio : IDisposable
{
    private readonly Dictionary<string, (WaveOutEvent Player, BufferedWaveProvider Buffer)> outputs = new();
    private readonly object gate = new();
    private WaveInEvent? input;
    public bool Listening { get; set; }
    public bool MicrophoneOn => input is not null;
    public event Action<byte[]>? Captured;
    public event Action<string>? Failed;

    public void StartMicrophone()
    {
        if (input is not null) return;
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("没有可用的麦克风，请检查 Windows 声音设置。");
        var next = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 40, NumberOfBuffers = 3 };
        next.DataAvailable += (_, e) => Captured?.Invoke(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
        next.RecordingStopped += (_, e) => { if (e.Exception is not null) Failed?.Invoke("麦克风已停止：" + e.Exception.Message); };
        try { next.StartRecording(); input = next; }
        catch { next.Dispose(); throw; }
    }

    public void StopMicrophone()
    {
        var old = input; input = null;
        if (old is null) return;
        try { old.StopRecording(); } finally { old.Dispose(); }
    }

    public void Play(string ip, byte[] data)
    {
        lock (gate)
        {
            if (!Listening) return;
            if (!outputs.TryGetValue(ip, out var output))
            {
                var buffer = new BufferedWaveProvider(new WaveFormat(16000, 16, 1))
                { BufferDuration = TimeSpan.FromMilliseconds(600), DiscardOnBufferOverflow = true, ReadFully = true };
                var player = new WaveOutEvent { DesiredLatency = 120 };
                try { player.Init(buffer); player.Play(); }
                catch { player.Dispose(); throw; }
                output = (player, buffer); outputs[ip] = output;
            }
            output.Buffer.AddSamples(data, 0, data.Length - data.Length % 2);
        }
    }

    public void Remove(string ip)
    {
        lock (gate) { if (outputs.Remove(ip, out var output)) output.Player.Dispose(); }
    }

    public void MutePlayback()
    {
        lock (gate)
        {
            Listening = false;
            foreach (var output in outputs.Values) output.Player.Dispose();
            outputs.Clear();
        }
    }

    public void Dispose() { StopMicrophone(); MutePlayback(); }
}
