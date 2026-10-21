using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Synqora.Services;

/// <summary>Bounded producer for the original endpoint or an explicitly identified VB-CABLE pair.</summary>
public sealed class MicrophoneBridge : IDisposable
{
    private const uint WritePcm = (0x1dU << 16) | (2U << 14) | (0x801U << 2);
    private const uint ClearPcm = (0x1dU << 16) | (2U << 14) | (0x802U << 2);
    private readonly object gate = new();
    private readonly Channel<(byte[] Pcm, long Generation)> packets = Channel.CreateBounded<(byte[], long)>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.DropOldest });
    private long generation;
    private SafeFileHandle? device;
    private WasapiOut? cable;
    private MMDevice? cableDevice;
    private readonly BufferedWaveProvider cablePcm = new(new WaveFormat(48000, 16, 1)) { BufferDuration = TimeSpan.FromMilliseconds(100), DiscardOnBufferOverflow = true, ReadFully = true };
    private bool streaming, disposed;
    private volatile bool routing;
    public bool Routing => routing;
    public string? Error { get; private set; }
    public string CaptureEndpoint { get; private set; } = "Windows microphone is not set up";
    public MicrophoneBridge() => _ = Task.Run(Pump);
    public void TryStart() { try { Start(); } catch (Exception error) { Error = error.Message; } }
    public void Start()
    {
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (routing) return;
            if (device != null || cable != null) Stop();
            var handle = Open(@"\\.\SynqoraMicrophone", 0x40000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) {
                var error = Marshal.GetLastWin32Error(); handle.Dispose();
                if (error is not (2 or 3)) throw new Win32Exception(error, "Couldn’t open the Synqora microphone. Another Synqora instance may be using it.");
                using var devices = new MMDeviceEnumerator();
                using var capture = devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).FirstOrDefault(IsCable);
                cableDevice = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => IsCable(d) && d.FriendlyName.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase));
                if (capture == null || cableDevice == null) { cableDevice?.Dispose(); cableDevice = null; throw new InvalidOperationException("Set up the bundled VB-CABLE microphone component, then restart Synqora. Speaker monitoring alone cannot provide a microphone to other apps."); }
                try {
                    cablePcm.ClearBuffer();
                    cable = new WasapiOut(cableDevice, AudioClientShareMode.Shared, true, 40);
                    cable.Init(cablePcm); cable.Play(); CaptureEndpoint = capture.FriendlyName;
                    cable.PlaybackStopped += (_, e) => { if (e.Exception != null) { Error = "Windows microphone routing stopped: " + e.Exception.Message; routing = false; } };
                    routing = true; Error = null; return;
                }
                catch { cable?.Dispose(); cable = null; cableDevice.Dispose(); cableDevice = null; throw; }
            }
            device = handle; CaptureEndpoint = "Synqora Microphone"; routing = true; Error = null;
        }
    }
    private static bool IsCable(MMDevice endpoint) => endpoint.DeviceFriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);
    public void Publish(byte[] pcm) { if (routing) packets.Writer.TryWrite((pcm, Volatile.Read(ref generation))); }
    public void Streaming(bool enabled)
    {
        lock (gate) {
            if (streaming != enabled) { streaming = enabled; Interlocked.Increment(ref generation); }
            if (!enabled) {
                while (packets.Reader.TryRead(out _)) { }
                cablePcm.ClearBuffer();
                if (device is { IsInvalid: false, IsClosed: false }) Control(device, ClearPcm, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            }
        }
    }
    private async Task Pump()
    {
        await foreach (var packet in packets.Reader.ReadAllAsync()) {
            lock (gate) {
                if (!routing || !streaming || packet.Generation != generation) continue;
                var pcm = packet.Pcm;
                if (cable != null) {
                    if (cablePcm.BufferedDuration.TotalMilliseconds > 80) cablePcm.ClearBuffer();
                    cablePcm.AddSamples(pcm, 0, pcm.Length); continue;
                }
                if (device == null) continue;
                // Wire packets are bounded at 16 KiB; the driver accepts at most 8 KiB.
                for (int offset = 0; offset < pcm.Length; offset += 8192) {
                    var count = Math.Min(8192, pcm.Length - offset);
                    var chunk = offset == 0 && count == pcm.Length ? pcm : pcm.AsSpan(offset, count).ToArray();
                    if (!Control(device, WritePcm, chunk, count, IntPtr.Zero, 0, out _, IntPtr.Zero)) {
                        Error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                        Stop(); break;
                    }
                }
            }
        }
    }
    public void Stop()
    {
        lock (gate) {
            routing = false;
            Interlocked.Increment(ref generation);
            while (packets.Reader.TryRead(out _)) { }
            device?.Dispose(); device = null; // Driver cleanup clears any buffered phone audio.
            cable?.Stop(); cable?.Dispose(); cable = null;
            cableDevice?.Dispose(); cableDevice = null; cablePcm.ClearBuffer();
        }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; Stop(); packets.Writer.TryComplete(); } }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle Open(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Control(SafeFileHandle device, uint code, byte[]? input, int length, IntPtr output, int outputLength, out int returned, IntPtr overlapped);
}
