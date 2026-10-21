using Synqora.Core;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

var sink = new TestInput();
await using var server = new SessionServer(sink);
server.Start(loopbackOnly: true);
if (args.Contains("--serve"))
{
    var index = Array.IndexOf(args, "--serve"); var folder = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(folder);
    server.AllowControls(true); // TestInput is an isolated measurement sink. It never calls SendInput.
    long firstVideo = 0, lastVideo = 0; int lastRotation = 0;
    server.Video += bytes => { var now = System.Diagnostics.Stopwatch.GetTimestamp(); if (firstVideo == 0) firstVideo = now; lastVideo = now; File.WriteAllBytes(Path.Combine(folder, "backend-camera.jpg"), bytes); };
    server.OrientedVideo += (_, rotation) => lastRotation = rotation;
    File.WriteAllText(Path.Combine(folder, "backend-invite.txt"), server.Invitation("127.0.0.1"));
    for (int i = 0; i < 300; i++) {
        var path = Path.Combine(folder, "backend-result.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { server.State, server.AudioBytes, server.VideoFrames, receivedVideoFps = lastVideo > firstVideo ? (server.VideoFrames - 1) * System.Diagnostics.Stopwatch.Frequency / (double)(lastVideo - firstVideo) : 0, lastRotation, server.InputCommands, sink.Moves, sink.Texts, sink.Keys, inputDocument = sink.Document, sink.MediaCommands, sink.Scrolls, sink.Clicks }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
        if (File.Exists(Path.Combine(folder, "backend-stop"))) break;
        await Task.Delay(1000);
    }
    return;
}

var invitation = new Uri(server.Invitation("127.0.0.1"));
var query = invitation.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
string key = query["key"], pin = query["pin"];
var token = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
async Task<(TcpClient Client, SslStream Stream)> Connect(string? expectedPin = null) {
    var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", server.Port, token);
    var stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate != null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == (expectedPin ?? pin));
    try { await stream.AuthenticateAsClientAsync("Synqora"); return (client, stream); }
    catch { stream.Dispose(); client.Dispose(); throw; }
}
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); Console.WriteLine("PASS: " + name); }
try { await Connect(new string('0', 64)); Check(false, "pin rejection"); } catch (System.Security.Authentication.AuthenticationException) { Check(true, "pin rejection"); }
var wrong = await Connect();
await Wire.Write(wrong.Stream, FrameKind.Hello, Wire.Encode(new { key = new string('0', 64), role = "control", name = "Probe" }), token);
try { await Wire.Read(wrong.Stream, token); Check(false, "wrong pairing key rejection"); } catch (IOException) { Check(true, "wrong pairing key rejection"); } wrong.Stream.Dispose(); wrong.Client.Dispose();
var control = await Connect();
await Wire.Write(control.Stream, FrameKind.Hello, Wire.Encode(new { key, role = "control", name = "Backend probe", battery = 83 }), token);
var welcome = await Wire.Read(control.Stream, token);
using var data = JsonDocument.Parse(welcome.Payload); var session = data.RootElement.GetProperty("session").GetString();
await Wire.Write(control.Stream, FrameKind.State, Wire.Encode(new { microphone = false, camera = false, latencyMs = (int?)null }), token);
await Task.Delay(100); Check(server.State.Connected, "initial null latency does not disconnect a new peer");
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "text", text = "denied" }), token);
await Task.Delay(100); Check(sink.Texts.Count == 0, "remote input denied until explicitly enabled");
server.AllowControls(true);
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "text", text = "Synqora backend test" }), token);
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "move", dx = 12, dy = -8 }), token);
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "media", command = "play" }), token);
await Wire.Write(control.Stream, FrameKind.Ping, Wire.Encode(new { timestamp = 123456L }), token);
var pong = await Wire.Read(control.Stream, token);
using var p = JsonDocument.Parse(pong.Payload);
Check(p.RootElement.GetProperty("timestamp").GetInt64() == 123456 && p.RootElement.GetProperty("controlsAllowed").GetBoolean(), "heartbeat and live capability synchronization");
Check(sink.Texts.SequenceEqual(["Synqora backend test"]) && sink.Moves == 1 && sink.MediaCommands == 1, "text, pointer and media commands delivered to isolated input sink");
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "edit", move = 0, before = 1, after = 0, text = "t!" }), token);
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "key", key = "enter", modifiers = 0 }), token);
await Wire.Write(control.Stream, FrameKind.Command, Wire.Encode(new { type = "key", key = "c", modifiers = 2 }), token);
await Task.Delay(100);
Check(sink.Document == "Synqora backend test!\n" && sink.Keys.Contains("2:c"), "live replacement, Enter and atomic shortcuts reach the sink");
var motion = new RelativeMotion(); int totalX = 0, totalY = 0;
for (int i = 0; i < 200; ++i) { var step = motion.Add(.15f, -.15f); totalX += step.X; totalY += step.Y; }
Check(totalX == 30 && totalY == -30, "fractional high-rate pointer movement is preserved");
var audio = await Connect(); await Wire.Write(audio.Stream, FrameKind.Hello, Wire.Encode(new { key, role = "audio", session }), token); await Wire.Read(audio.Stream, token);
byte[] pcm = [0, 0, 255, 127, 0, 128, 0, 0]; byte[]? received = null; server.Audio += bytes => received = bytes;
await Wire.Write(audio.Stream, FrameKind.Audio, pcm, token); await Task.Delay(100);
Check(received != null && received.SequenceEqual(pcm), "PCM delivery is byte exact over separate authenticated TLS lane");
var video = await Connect(); await Wire.Write(video.Stream, FrameKind.Hello, Wire.Encode(new { key, role = "video", session }), token); await Wire.Read(video.Stream, token);
var jpeg = new byte[] { 255,216,255,192,0,8,8,0,2,0,2,0 };
var oriented = new byte[jpeg.Length + 8]; "SQJ1"u8.CopyTo(oriented); oriented[4] = 1; jpeg.CopyTo(oriented, 8);
int deliveredRotation = -1; byte[]? deliveredJpeg = null;
server.OrientedVideo += (bytes, rotation) => { deliveredJpeg = bytes; deliveredRotation = rotation; };
await Wire.Write(video.Stream, FrameKind.OrientedVideo, oriented, token); await Task.Delay(100);
Check(deliveredRotation == 90 && deliveredJpeg!.SequenceEqual(jpeg), "oriented JPEG envelope is validated and separated before decoding");
video.Stream.Dispose(); video.Client.Dispose();
using var oversized = new MemoryStream([4, 127, 255, 255, 255]);
try { await Wire.Read(oversized, token); Check(false, "oversized frame rejected"); } catch (InvalidDataException) { Check(true, "oversized frame rejected before allocation"); }
Check(!Wire.ValidJpeg([1, 2, 3]) && !Wire.ValidJpeg([255, 216, 255, 192, 0, 8, 8, 255, 255, 255, 255, 0]), "malformed and oversized JPEG rejected");
audio.Stream.Dispose(); audio.Client.Dispose(); control.Stream.Dispose(); control.Client.Dispose();
await Task.Delay(100); Check(!server.State.Connected, "disconnect revokes connection and media state");
Console.WriteLine("Backend protocol checks passed.");

sealed class TestInput : IInputSink {
    private readonly object gate = new();
    private readonly System.Text.StringBuilder document = new();
    private int cursor;
    public string Document { get { lock (gate) return document.ToString(); } }
    public int Moves, MediaCommands, Scrolls, Clicks;
    public System.Collections.Concurrent.ConcurrentQueue<string> Texts { get; } = new();
    public System.Collections.Concurrent.ConcurrentQueue<string> Keys { get; } = new();
    public void Move(float dx, float dy) => Interlocked.Increment(ref Moves);
    public void Click(bool right) => Interlocked.Increment(ref Clicks);
    public void Scroll(float delta) => Interlocked.Increment(ref Scrolls);
    public void Text(string text) { Texts.Enqueue(text); lock (gate) { document.Insert(cursor, text); cursor += text.Length; } }
    public void Key(string key, int modifiers) {
        Keys.Enqueue($"{modifiers}:{key}"); if (modifiers != 0) return;
        lock (gate) {
            if (key == "backspace" && cursor > 0) { int count = cursor >= 2 && char.IsLowSurrogate(document[cursor-1]) && char.IsHighSurrogate(document[cursor-2]) ? 2 : 1; document.Remove(cursor-count,count); cursor -= count; }
            else if (key == "delete" && cursor < document.Length) document.Remove(cursor,1);
            else if (key == "left" && cursor > 0) cursor--;
            else if (key == "right" && cursor < document.Length) cursor++;
            else if (key is "enter" or "tab") { document.Insert(cursor,key == "enter" ? "\n" : "\t"); cursor++; }
        }
    }
    public void Media(string command) => Interlocked.Increment(ref MediaCommands);
    public void Volume(float value) { }
}
