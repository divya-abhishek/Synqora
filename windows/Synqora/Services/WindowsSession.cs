using NAudio.CoreAudioApi;
using NAudio.Wave;
using Synqora.Core;
using System.Runtime.InteropServices;

namespace Synqora.Services;

public sealed class WindowsSession : ISynqoraSession, IDisposable
{
    public SessionServer Server { get; }
    public DeviceSnapshot Device { get; private set; } = new("No phone connected", "No device paired", ConnectionPhase.Disconnected);
    public bool CanStream => Server.State.Connected;
    public event Action<DeviceSnapshot>? Changed;
    public event Action<byte[], int>? Video;
    public event Action<float[]>? Levels;
    private readonly BufferedWaveProvider audio = new(new WaveFormat(48000, 16, 1)) { BufferDuration = TimeSpan.FromMilliseconds(160), DiscardOnBufferOverflow = true };
    private WasapiOut? playback;
    private MMDevice? monitorDevice;
    public bool Monitoring => playback != null;
    public float Gain { get; set; } = 1f;
    public bool Muted { get; set; }
    public MicrophoneBridge MicrophoneOutput { get; } = new();
    private readonly float[] history = new float[64];
    private long lastMeter;
    private bool microphoneWasActive;
    public WindowsSession()
    {
        Server = new(new WindowsInput());
        Server.Changed += state => {
            var active = state.Connected && state.Microphone;
            MicrophoneOutput.Streaming(active);
            if (active && !microphoneWasActive && !MicrophoneOutput.Routing) _ = Task.Run(MicrophoneOutput.TryStart);
            microphoneWasActive = active;
            if (!active) audio.ClearBuffer();
            Device = new(state.Name, state.Connected ? "Android phone · encrypted LAN" : "No device paired", state.Connected ? ConnectionPhase.Connected : ConnectionPhase.Disconnected, state.LatencyMs, state.Battery); Changed?.Invoke(Device);
        };
        Server.Audio += bytes => {
            if (Muted || Math.Abs(Gain - 1) > .001f) {
                bytes = (byte[])bytes.Clone();
                for (int i = 0; i + 1 < bytes.Length; i += 2) {
                    var sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2));
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i, 2), Muted ? (short)0 : (short)Math.Clamp((int)(sample * Gain), short.MinValue, short.MaxValue));
                }
            }
            if (playback != null) audio.AddSamples(bytes, 0, bytes.Length);
            MicrophoneOutput.Publish(bytes);
            float peak = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2))) / 32768f);
            Array.Copy(history, 1, history, 0, 63); history[63] = peak;
            var now = Environment.TickCount64;
            if (now - lastMeter >= 33) { lastMeter = now; Levels?.Invoke((float[])history.Clone()); }
        };
        Server.OrientedVideo += (bytes, rotation) => Video?.Invoke(bytes, rotation);
    }
    public void Start(int port = 58739, bool loopbackOnly = false) => Server.Start(port, loopbackOnly);
    public void Monitor(bool enabled)
    {
        playback?.Dispose(); playback = null; monitorDevice?.Dispose(); monitorDevice = null; audio.ClearBuffer();
        if (enabled) {
            using var devices = new MMDeviceEnumerator(); monitorDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (monitorDevice.DeviceFriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)) { monitorDevice.Dispose(); monitorDevice = null; throw new InvalidOperationException("Choose your speakers or headphones as the Windows output before enabling speaker monitoring."); }
            try { playback = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 60); playback.Init(audio); playback.Play(); }
            catch { playback?.Dispose(); playback = null; monitorDevice.Dispose(); monitorDevice = null; throw; }
        }
    }
    public Task DisconnectAsync() { Monitor(false); Server.Disconnect(); return Task.CompletedTask; }
    public void Dispose() { Monitor(false); Server.DisposeAsync().GetAwaiter().GetResult(); MicrophoneOutput.Dispose(); }
}

public sealed class WindowsInput : IInputSink
{
    private readonly RelativeMotion pointer = new();
    private readonly RelativeMotion wheel = new();
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    private static void Send(params Input[] inputs) { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new IOException("Windows blocked remote input (for example, an elevated target)."); }
    public void Move(float dx, float dy) { var value = pointer.Add(dx, dy); if (value != (0, 0)) Send(new Input { Data = new() { Mouse = new() { X = value.X, Y = value.Y, Flags = 1 | 0x2000 } } }); }
    public void Click(bool right) => Send(new Input { Data = new() { Mouse = new() { Flags = right ? 8u : 2u } } }, new Input { Data = new() { Mouse = new() { Flags = right ? 16u : 4u } } });
    public void Scroll(float delta) { var value = wheel.Add(delta, 0).X; if (value != 0) Send(new Input { Data = new() { Mouse = new() { MouseData = unchecked((uint)value), Flags = 0x800 } } }); }
    public void Text(string text) { foreach (char c in text) Send(new Input { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 4 } } }, new Input { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 6 } } }); }
    public void Key(string key, int modifiers)
    {
        ushort code = key switch { "enter" => 13, "tab" => 9, "escape" => 27, "backspace" => 8, "delete" => 46, "left" => 37, "up" => 38, "right" => 39, "down" => 40, "home" => 36, "end" => 35, "pageup" => 33, "pagedown" => 34, "space" => 32, _ when key.Length == 1 => char.ToUpperInvariant(key[0]), _ when key.StartsWith('f') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 12 => (ushort)(111 + f), _ => throw new ArgumentException("Unknown key") };
        Input Stroke(ushort value, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Key = value, Flags = (up ? 2u : 0) | (value is >= 33 and <= 46 ? 1u : 0) } } };
        ushort[] held = new[] { (Bit: 1, Code: (ushort)16), (Bit: 2, Code: (ushort)17), (Bit: 4, Code: (ushort)18) }.Where(m => (modifiers & m.Bit) != 0).Select(m => m.Code).ToArray();
        var strokes = held.Select(k => Stroke(k, false)).Concat([Stroke(code, false), Stroke(code, true)]).Concat(held.Reverse().Select(k => Stroke(k, true))).ToArray();
        try { Send(strokes); }
        catch { if (held.Length != 0) Send(held.Select(k => Stroke(k, true)).ToArray()); throw; }
    }
    public void Media(string command)
    {
        ushort key = command switch { "next" => 0xb0, "previous" => 0xb1, _ => 0xb3 };
        Send(new Input { Type = 1, Data = new() { Keyboard = new() { Key = key } } }, new Input { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = 2 } } });
    }
    public void Volume(float level) { using var devices = new MMDeviceEnumerator(); using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = level; }
}
