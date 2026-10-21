using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using Windows.Graphics.Imaging;

namespace Synqora.Services;

/// <summary>The independent Media Foundation consumer receives pixels even when the UI is detached.</summary>
public sealed class CameraBridge : IDisposable
{
    private readonly object gate = new();
    private readonly object cameraGate = new();
    private IntPtr writer, publishedCamera;
    private volatile bool disposed;
    private bool streaming;
    private long generation;
    public long Generation => Volatile.Read(ref generation);
    private readonly string path;
    public bool Published => Volatile.Read(ref publishedCamera) != IntPtr.Zero;
    public static bool Installed {
        get {
            using var registration = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Classes\CLSID\{57FD6F94-9413-4E47-A4CA-C0AFD738D152}\InProcServer32");
            return registration?.GetValue(null) is string dll && File.Exists(dll);
        }
    }
    public CameraBridge()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synqora", "Camera");
        Directory.CreateDirectory(folder);
        path = Path.Combine(folder, $"frame-{Guid.NewGuid():N}.bin");
        Marshal.ThrowExceptionForHR(Create(path, out writer));
    }
    public void Streaming(bool enabled)
    {
        lock (gate) {
            if (disposed || streaming == enabled) return;
            streaming = enabled; Interlocked.Increment(ref generation);
            if (!enabled) ClearNative(writer);
        }
    }
    public void Write(SoftwareBitmap frame, long frameGeneration)
    {
        int length = checked(frame.PixelWidth * frame.PixelHeight * 4);
        var pixels = ArrayPool<byte>.Shared.Rent(length);
        try {
            frame.CopyToBuffer(pixels.AsBuffer(0, length));
            lock (gate) {
                if (!disposed && streaming && frameGeneration == generation) Marshal.ThrowExceptionForHR(Publish(writer, pixels, (uint)length, (uint)frame.PixelWidth, (uint)frame.PixelHeight, (uint)frame.PixelWidth * 4));
            }
        } finally { ArrayPool<byte>.Shared.Return(pixels, clearArray: true); }
    }
    public void Clear() { lock (gate) if (!disposed) ClearNative(writer); }
    public void Framing(bool fill) { lock (gate) if (!disposed) SetFraming(writer, fill); }
    public void Start()
    {
        lock (cameraGate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (publishedCamera == IntPtr.Zero) {
                Marshal.ThrowExceptionForHR(StartNative(path, out publishedCamera));
                try { ValidateWindowsCapture(); }
                catch { StopNative(publishedCamera); publishedCamera = IntPtr.Zero; throw; }
            }
        }
    }
    private static void ValidateWindowsCapture()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Synqora.CameraCheck.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The Windows camera diagnostic component is missing. Reinstall Synqora.");
        using var process = Process.Start(new ProcessStartInfo(executable, "--observe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new IOException("Could not verify the Windows camera.");
        if (!process.WaitForExit(15000)) { process.Kill(); throw new IOException("Windows camera activation timed out. Close other camera apps and try again."); }
        var output = process.StandardOutput.ReadToEnd();
        StartupDiagnostics.Write("Windows camera capture verification: " + output.Trim());
        if (process.ExitCode != 0) throw new IOException($"Windows could not capture from Synqora Camera (0x{process.ExitCode:X8}). Reinstall the latest Synqora build and check Camera privacy settings.");
    }
    public void Stop() { lock (cameraGate) { if (publishedCamera != IntPtr.Zero) StopNative(publishedCamera); publishedCamera = IntPtr.Zero; } }
    public SourceSample Inspect()
    {
        // Holding the writer lifetime prevents deletion while a test consumer opens its own map.
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            Marshal.ThrowExceptionForHR(InspectNative(path, out var sample)); return sample;
        }
    }
    public void Dispose() {
        lock (cameraGate) {
            lock (gate) { if (disposed) return; disposed = true; }
            Stop();
            lock (gate) { Destroy(writer); writer = IntPtr.Zero; }
        }
    }
    [StructLayout(LayoutKind.Sequential)] public struct SourceSample {
        public uint Width, Height, Bytes;
        public long Timestamp;
        public ulong Checksum;
        public byte MinimumLuma, MaximumLuma;
    }
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraCreate", CharSet = CharSet.Unicode)] private static extern int Create(string path, out IntPtr writer);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraWrite")] private static extern int Publish(IntPtr writer, byte[] pixels, uint bytes, uint width, uint height, uint stride);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraClear")] private static extern void ClearNative(IntPtr writer);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraSetFraming")] private static extern void SetFraming(IntPtr writer, [MarshalAs(UnmanagedType.Bool)] bool fill);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraDestroy")] private static extern void Destroy(IntPtr writer);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraStart", CharSet = CharSet.Unicode)] private static extern int StartNative(string path, out IntPtr camera);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraStop")] private static extern void StopNative(IntPtr camera);
    [DllImport("Synqora.Camera.dll", EntryPoint = "SynqoraCameraInspect", CharSet = CharSet.Unicode)] private static extern int InspectNative(string path, out SourceSample sample);
}
