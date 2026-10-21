using System.Security.Cryptography;
using System.Text.Json;

namespace Synqora.Core;

public sealed record Transfer(string Id, string Name, long Size, bool Incoming, string Status = "Waiting", long Done = 0, string? Path = null);

/// <summary>Consent and integrity for one bounded file lane, independent of media and input.</summary>
public sealed class FileExchange
{
    private sealed record Entry(Transfer View, string Hash, string? Source = null, string? Destination = null);
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private readonly Dictionary<string, (string Directory, DateTimeOffset Expires)> requested = [];
    public void ReserveReceive(string request, string directory) { lock (gate) { foreach (var old in requested.Where(p => p.Value.Expires < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) requested.Remove(old); if (requested.Count >= 10) throw new IOException("Wait for your pending photos to transfer."); requested[request] = (directory, DateTimeOffset.UtcNow.AddMinutes(2)); } }
    private readonly SessionServer server;
    public event Action? Changed;
    public IReadOnlyList<Transfer> Transfers { get { lock (gate) return entries.Values.Select(e => e.View).Reverse().ToArray(); } }
    public FileExchange(SessionServer server) {
        this.server = server; server.FileLane = Serve;
        server.Companion += Receive;
        server.Changed += state => { if (!state.Connected) {
            lock (gate) { requested.Clear(); foreach (var (id, e) in entries.ToArray()) if (e.View.Status is "Waiting" or "Ready" or "Transferring") entries[id] = e with { View = e.View with { Status = "Disconnected" }, Source = null, Destination = null }; }
            Changed?.Invoke();
        } };
    }
    public async Task Offer(string path, CancellationToken ct = default) {
        var info = new FileInfo(path);
        if (!server.State.Connected) throw new InvalidOperationException("Connect a phone first.");
        if (!info.Exists) throw new IOException("This file is no longer available.");
        using var input = File.OpenRead(path);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        string id = Guid.NewGuid().ToString("N"), name = SafeName(info.Name);
        lock (gate) { MakeRoom(); entries[id] = new(new(id, name, info.Length, false), hash, Source: path); }
        if (!server.SendCompanion(new { type = "fileOffer", id, name, size = info.Length, hash })) { Reject(id, "Disconnected"); throw new IOException("The phone disconnected."); }
        Changed?.Invoke();
    }
    public void Accept(string id, string directory) {
        if (!server.State.Connected) return;
        lock (gate) {
            if (!entries.TryGetValue(id, out var e) || !e.View.Incoming || e.View.Status != "Waiting") return;
            Directory.CreateDirectory(directory);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
            if (drive.AvailableFreeSpace < e.View.Size) throw new IOException("Not enough free space to receive this file.");
            // Source filenames never choose a destination directory, and existing files are never overwritten.
            string path = UniquePath(directory, e.View.Name);
            entries[id] = e with { View = e.View with { Status = "Ready" }, Destination = path };
        }
        server.SendCompanion(new { type = "fileAccept", id });
        Changed?.Invoke();
    }
    public void Decline(string id) { Reject(id, "Declined"); server.SendCompanion(new { type = "fileCancel", id }); }
    private void Reject(string id, string status) { lock (gate) if (entries.TryGetValue(id, out var e)) entries[id] = e with { View = e.View with { Status = status }, Source = null, Destination = null }; Changed?.Invoke(); }
    private void Receive(JsonElement json) {
        try {
            string type = json.GetProperty("type").GetString() ?? "";
            if (!type.StartsWith("file", StringComparison.Ordinal)) return;
            string id = json.GetProperty("id").GetString() ?? "";
            if (!Guid.TryParseExact(id, "N", out _)) return;
            if (type == "fileOffer") {
                var name = SafeName(json.GetProperty("name").GetString() ?? "file");
                long size = json.GetProperty("size").GetInt64(); string hash = json.GetProperty("hash").GetString() ?? "";
                if (size < 0 || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return;
                string? destination = null;
                lock (gate) {
                    if (entries.ContainsKey(id)) return; MakeRoom(); entries[id] = new(new(id, name, size, true), hash);
                    if (json.TryGetProperty("request", out var request) && request.ValueKind == JsonValueKind.String && requested.Remove(request.GetString() ?? "", out var reservation) && reservation.Expires > DateTimeOffset.UtcNow) destination = reservation.Directory;
                }
                if (destination != null) Accept(id, destination);
                Changed?.Invoke();
            } else if (type == "fileCancel") Reject(id, "Cancelled");
        } catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or IOException) { }
    }
    private async Task Serve(Stream stream, JsonElement hello, CancellationToken ct) {
        string id = hello.GetProperty("transfer").GetString() ?? "";
        Entry entry;
        lock (gate) {
            entry = entries.GetValueOrDefault(id) ?? throw new InvalidDataException("Unknown transfer.");
            bool upload = hello.GetProperty("direction").GetString() == "upload";
            if (upload != entry.View.Incoming || (upload ? entry.View.Status != "Ready" : entry.View.Status != "Waiting") || entry.Source == null && entry.Destination == null) throw new InvalidDataException("Transfer not accepted.");
            entries[id] = entry with { View = entry.View with { Status = "Transferring" } };
        }
        Changed?.Invoke(); string? partial = null;
        try {
            if (entry.View.Incoming) {
                partial = entry.Destination + "." + id + ".part";
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long done = 0;
                    while (true) {
                        EnsureActive(id); var frame = await ReadTimed(stream, ct);
                        if (frame.Kind == FrameKind.FileEnd) { if (frame.Payload.Length != 0) throw new InvalidDataException("Invalid file terminator."); break; }
                        if (frame.Kind != FrameKind.FileChunk || frame.Payload.Length == 0 || frame.Payload.Length > entry.View.Size - done) throw new InvalidDataException("Invalid file length.");
                        await output.WriteAsync(frame.Payload, ct); hash.AppendData(frame.Payload); done += frame.Payload.Length; Progress(id, done);
                    }
                    if (done != entry.View.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(entry.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("File integrity check failed.");
                    await output.FlushAsync(ct);
                }
                EnsureActive(id); File.Move(partial, entry.Destination!); partial = null;
                await Wire.Write(stream, FrameKind.FileEnd, ReadOnlyMemory<byte>.Empty, ct);
                Complete(id, entry.Destination);
            } else {
                await using var input = File.OpenRead(entry.Source!); byte[] buffer = new byte[65536]; long done = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0) { EnsureActive(id); await Wire.Write(stream, FrameKind.FileChunk, buffer.AsMemory(0, count), ct); done += count; if (done > entry.View.Size) throw new IOException("The source file changed."); Progress(id, done); }
                if (done != entry.View.Size) throw new IOException("The source file changed.");
                await Wire.Write(stream, FrameKind.FileEnd, ReadOnlyMemory<byte>.Empty, ct);
                var acknowledgment = await ReadTimed(stream, ct);
                if (acknowledgment.Kind != FrameKind.FileEnd || acknowledgment.Payload.Length != 0) throw new IOException("Phone did not confirm delivery.");
                Complete(id, null);
            }
        } catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException or UnauthorizedAccessException) { Reject(id, "Failed"); }
        finally { if (partial != null) try { File.Delete(partial); } catch (IOException) { } }
    }
    private void EnsureActive(string id) { lock (gate) if (!entries.TryGetValue(id, out var e) || e.View.Status != "Transferring") throw new IOException("Transfer cancelled."); }
    private void Progress(string id, long done) { bool notify; lock (gate) { var e = entries[id]; notify = done == e.View.Size || done / (256 * 1024) != e.View.Done / (256 * 1024); entries[id] = e with { View = e.View with { Done = done } }; } if (notify) Changed?.Invoke(); }
    private void Complete(string id, string? path) { lock (gate) { var e = entries[id]; entries[id] = e with { View = e.View with { Status = "Complete", Path = path }, Source = null, Destination = null }; } Changed?.Invoke(); }
    private void MakeRoom() { if (entries.Count >= 50) { var old = entries.FirstOrDefault(e => e.Value.View.Status is not ("Waiting" or "Ready" or "Transferring")); if (old.Key == null) throw new IOException("Too many pending files."); entries.Remove(old.Key); } }
    private static async Task<Frame> ReadTimed(Stream stream, CancellationToken ct) { using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(30)); return await Wire.Read(stream, limit.Token); }
    public static string SafeName(string name) {
        if (name.Length is < 1 or > 180 || name is "." or ".." || name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || name.EndsWith('.') || name.EndsWith(' ')) throw new InvalidDataException("Invalid filename.");
        string stem = name.Split('.')[0].ToUpperInvariant(); if (stem is "CON" or "PRN" or "AUX" or "NUL" || Enumerable.Range(1, 9).Any(i => stem == $"COM{i}" || stem == $"LPT{i}")) throw new InvalidDataException("Reserved filename.");
        return name;
    }
    private static string UniquePath(string directory, string name) { string path = Path.Combine(directory, name); for (int i = 1; File.Exists(path); i++) path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}"); return path; }
}
