using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Synqora.Core;

public sealed record PeerState(string Name = "", bool Connected = false, bool Microphone = false, bool Camera = false, bool ControlsAllowed = false, int? Battery = null, int? LatencyMs = null, string? Error = null);
public interface IInputSink { void Move(float dx, float dy); void Click(bool right); void Scroll(float delta); void Text(string text); void Key(string key, int modifiers); void Media(string command); void Volume(float level); }

// Three separately authenticated TLS lanes prevent video backpressure from holding up input/audio.
public sealed class SessionServer(IInputSink input) : IAsyncDisposable
{
    private readonly X509Certificate2 certificate = CreateCertificate();
    private string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly HashSet<string> mediaRoles = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim slots = new(8);
    private readonly object gate = new();
    private readonly HashSet<TcpClient> clients = [];
    private TcpListener? listener;
    private string? sessionId;
    private long sessionAudioOrigin, sessionVideoOrigin;
    private DateTimeOffset expires;
    private PeerState state = new();
    public PeerState State { get { lock (gate) return state; } }
    public int Port { get; private set; }
    public long AudioBytes { get; private set; }
    public long VideoFrames { get; private set; }
    public long InputCommands { get; private set; }
    public event Action<PeerState>? Changed;
    public event Action<byte[]>? Audio;
    public event Action<byte[]>? Video;
    public event Action<byte[], int>? OrientedVideo;
    public event Action<string>? Diagnostic;
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(certificate.RawData));
    public string Invitation(string? host = null) { lock (gate) { if (sessionId == null) { secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); expires = DateTimeOffset.UtcNow.AddMinutes(10); } return $"synqora://pair?host={Uri.EscapeDataString(host ?? LocalAddress())}&port={Port}&pin={Fingerprint}&key={secret}"; } }
    public void Start(int port = 0, bool loopbackOnly = false)
    {
        if (listener != null) return;
        expires = DateTimeOffset.UtcNow.AddMinutes(10);
        listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port); listener.Start(8);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(Accept);
    }
    public void AllowControls(bool allow) => Update(s => s with { ControlsAllowed = allow });
    public void Disconnect()
    {
        lock (gate) { foreach (var client in clients) client.Close(); sessionId = null; mediaRoles.Clear(); Update(_ => new()); }
    }
    private async Task Accept()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(lifetime.Token);
                if (!await slots.WaitAsync(0, lifetime.Token)) { client.Dispose(); continue; }
                lock (gate) clients.Add(client);
                _ = Task.Run(async () => { try { await Serve(client); } catch (Exception e) when (e is IOException or AuthenticationException or JsonException or OperationCanceledException or InvalidDataException or SocketException or KeyNotFoundException or InvalidOperationException or ArgumentException) { Diagnostic?.Invoke(e.GetType().Name); } finally { lock (gate) clients.Remove(client); client.Dispose(); slots.Release(); } });
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException) { }
    }
    private async Task Serve(TcpClient client)
    {
        client.NoDelay = true;
        using var stream = new SslStream(client.GetStream(), false);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); handshake.CancelAfter(TimeSpan.FromSeconds(8));
        await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, ClientCertificateRequired = false }, handshake.Token);
        var helloFrame = await Wire.Read(stream, handshake.Token);
        if (helloFrame.Kind != FrameKind.Hello) throw new InvalidDataException("Expected hello.");
        using var hello = JsonDocument.Parse(helloFrame.Payload);
        var h = hello.RootElement;
        string key = h.GetProperty("key").GetString() ?? "";
        string role = h.GetProperty("role").GetString() ?? "";
        bool control = role == "control";
        string ownSession;
        lock (gate)
        {
            if (key.Length != secret.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key), Encoding.ASCII.GetBytes(secret))) throw new AuthenticationException("Pairing key rejected.");
            if (control)
            {
                if (sessionId != null || DateTimeOffset.UtcNow > expires) throw new AuthenticationException("Pairing expired or another device is connected.");
                sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                sessionAudioOrigin = AudioBytes; sessionVideoOrigin = VideoFrames;
            }
            else if (role is not ("audio" or "video") || sessionId == null || h.GetProperty("session").GetString() != sessionId || !mediaRoles.Add(role)) throw new AuthenticationException("Media lane rejected.");
            ownSession = sessionId!;
        }
        try
        {
            await Wire.Write(stream, FrameKind.Welcome, Wire.Encode(new { session = ownSession, name = Environment.MachineName, controlsAllowed = State.ControlsAllowed, sampleRate = 48000, channels = 1 }), lifetime.Token);
            if (control) UpdateSession(ownSession, s => s with { Connected = true, Name = Clean(h.TryGetProperty("name", out var n) ? n.GetString() : null), Battery = h.TryGetProperty("battery", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out var battery) ? Math.Clamp(battery, 0, 100) : null, Error = null });
            int commands = 0; long second = Environment.TickCount64 / 1000;
            while (!lifetime.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var frame = await Wire.Read(stream, timeout.Token);
                lock (gate) if (sessionId != ownSession) break;
                if (frame.Kind == FrameKind.Ping) {
                    using var ping = JsonDocument.Parse(frame.Payload);
                    byte[] status;
                    lock (gate) status = Wire.Encode(new { timestamp = ping.RootElement.GetProperty("timestamp").GetInt64(), controlsAllowed = state.ControlsAllowed, receivedAudioBytes = AudioBytes - sessionAudioOrigin, receivedVideoFrames = VideoFrames - sessionVideoOrigin });
                    await Wire.Write(stream, FrameKind.Pong, status, lifetime.Token); continue;
                }
                if (role == "audio" && frame.Kind == FrameKind.Audio)
                {
                    if (frame.Payload.Length % 2 != 0) throw new InvalidDataException("Invalid PCM.");
                    lock (gate) { if (sessionId != ownSession) break; AudioBytes += frame.Payload.Length; Audio?.Invoke(frame.Payload); } continue;
                }
                if (role == "video" && frame.Kind is FrameKind.Video or FrameKind.OrientedVideo)
                {
                    int rotation = 0;
                    var jpeg = frame.Payload;
                    if (frame.Kind == FrameKind.OrientedVideo) {
                        if (jpeg.Length < 8 || !jpeg.AsSpan(0, 4).SequenceEqual("SQJ1"u8) || jpeg[4] > 3 || jpeg[5] != 0 || jpeg[6] != 0 || jpeg[7] != 0) throw new InvalidDataException("Invalid camera orientation header.");
                        rotation = jpeg[4] * 90; jpeg = jpeg.AsSpan(8).ToArray();
                    }
                    if (!Wire.ValidJpeg(jpeg)) throw new InvalidDataException("Invalid JPEG dimensions.");
                    lock (gate) { if (sessionId != ownSession) break; VideoFrames++; Video?.Invoke(jpeg); OrientedVideo?.Invoke(jpeg, rotation); } continue;
                }
                if (!control) throw new InvalidDataException("Unexpected media lane frame.");
                if (frame.Kind == FrameKind.State)
                {
                    using var doc = JsonDocument.Parse(frame.Payload); var s = doc.RootElement;
                    UpdateSession(ownSession, p => p with { Microphone = s.GetProperty("microphone").GetBoolean(), Camera = s.GetProperty("camera").GetBoolean(), LatencyMs = s.TryGetProperty("latencyMs", out var latency) && latency.ValueKind == JsonValueKind.Number && latency.TryGetInt32(out var ms) ? Math.Clamp(ms, 0, 10000) : null }); continue;
                }
                if (frame.Kind != FrameKind.Command) throw new InvalidDataException("Unexpected control frame.");
                var now = Environment.TickCount64 / 1000; if (now != second) { second = now; commands = 0; }
                if (++commands > 500) throw new InvalidDataException("Input rate exceeded.");
                if (!State.ControlsAllowed) continue;
                using var command = JsonDocument.Parse(frame.Payload); var c = command.RootElement;
                float Number(string field, float max) { float v = c.GetProperty(field).GetSingle(); if (!float.IsFinite(v) || Math.Abs(v) > max) throw new InvalidDataException("Invalid input value."); return v; }
                switch (c.GetProperty("type").GetString())
                {
                    case "move": input.Move(Number("dx", 2000), Number("dy", 2000)); break;
                    case "click": input.Click(c.GetProperty("right").GetBoolean()); break;
                    case "scroll": input.Scroll(Number("delta", 2000)); break;
                    case "text": var text = c.GetProperty("text").GetString() ?? ""; if (text.Length > 4096) throw new InvalidDataException("Text too long."); input.Text(text); break;
                    case "key":
                        var keyName = c.GetProperty("key").GetString() ?? "";
                        var modifiers = c.TryGetProperty("modifiers", out var mod) ? mod.GetInt32() : 0;
                        if (!RemoteKeys.Contains(keyName) || modifiers < 0 || modifiers > 7) throw new InvalidDataException("Unknown keyboard key or modifiers.");
                        input.Key(keyName, modifiers); break;
                    case "edit":
                        int move = c.GetProperty("move").GetInt32(), before = c.GetProperty("before").GetInt32(), after = c.GetProperty("after").GetInt32();
                        var insertion = c.GetProperty("text").GetString() ?? "";
                        if (Math.Abs((long)move) > 256 || before < 0 || before > 256 || after < 0 || after > 256 || insertion.Length > 1024) throw new InvalidDataException("Keyboard edit is too large.");
                        for (int i = 0; i < Math.Abs(move); ++i) input.Key(move < 0 ? "left" : "right", 0);
                        for (int i = 0; i < before; ++i) input.Key("backspace", 0);
                        for (int i = 0; i < after; ++i) input.Key("delete", 0);
                        if (insertion.Length != 0) input.Text(insertion); break;
                    case "media": var media = c.GetProperty("command").GetString() ?? ""; if (media is not ("play" or "next" or "previous")) throw new InvalidDataException("Unknown media command."); input.Media(media); break;
                    case "volume": input.Volume(Math.Clamp(Number("level", 1), 0, 1)); break;
                    default: throw new InvalidDataException("Unknown command.");
                }
                InputCommands++;
            }
        }
        finally
        {
            lock (gate) {
                if (sessionId == ownSession) {
                    if (control) { sessionId = null; mediaRoles.Clear(); foreach (var peer in clients.Where(p => p != client)) peer.Close(); Update(_ => new()); }
                    else { mediaRoles.Remove(role); Update(s => role == "audio" ? s with { Microphone = false } : s with { Camera = false }); }
                }
            }
        }
    }
    private static readonly HashSet<string> RemoteKeys = new(new[] { "enter", "tab", "escape", "backspace", "delete", "left", "right", "up", "down", "home", "end", "pageup", "pagedown", "space" }
        .Concat(Enumerable.Range('a', 26).Select(c => ((char)c).ToString()))
        .Concat(Enumerable.Range('0', 10).Select(c => ((char)c).ToString()))
        .Concat(Enumerable.Range(1, 12).Select(f => $"f{f}")), StringComparer.Ordinal);
    private void UpdateSession(string owner, Func<PeerState, PeerState> change) { lock (gate) if (sessionId == owner) Update(change); }
    // Serialize notifications with state changes so a retiring session cannot publish a stale snapshot.
    private void Update(Func<PeerState, PeerState> change) { lock (gate) { state = change(state); Changed?.Invoke(state); } }
    private static string Clean(string? name) => new((name ?? "Android phone").Where(c => !char.IsControl(c)).Take(64).ToArray());
    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=Synqora", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
    private static string LocalAddress() => NetworkInterface.GetAllNetworkInterfaces().Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(i => i.GetIPProperties().UnicastAddresses).Select(a => a.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))?.ToString() ?? "127.0.0.1";
    public ValueTask DisposeAsync() { lifetime.Cancel(); listener?.Stop(); Disconnect(); certificate.Dispose(); return ValueTask.CompletedTask; }
}
