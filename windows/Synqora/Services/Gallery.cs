using System.Text.Json;
using Synqora.Core;
namespace Synqora.Services;

public sealed record PhoneMedia(string Id, string Name, long Size, bool Video, long Time);
public sealed class Gallery
{
    private readonly SessionServer server;
    private readonly FileExchange files;
    private readonly object gate = new();
    private List<PhoneMedia> items = [];
    private readonly Dictionary<string, byte[]> thumbs = [];
    public bool Enabled { get; private set; }
    public bool More { get; private set; }
    public int Offset { get; private set; }
    public string Message { get; private set; } = "Enable photo sharing in Synqora on your phone.";
    public IReadOnlyList<PhoneMedia> Items { get { lock (gate) return items.ToArray(); } }
    public byte[]? Thumbnail(string id) { lock (gate) return thumbs.GetValueOrDefault(id); }
    public event Action<string?>? Changed;
    public Gallery(SessionServer server, FileExchange files) {
        this.server = server; this.files = files; server.Companion += Receive;
        server.Changed += s => { if (!s.Connected) { lock (gate) { Enabled = false; items.Clear(); thumbs.Clear(); Message = "Connect your phone to browse photos and videos."; } Changed?.Invoke(null); } };
    }
    public void Page(int offset = 0) { server.SendCompanion(new { type = "galleryPage", offset = Math.Max(0, offset) }); }
    public void Original(PhoneMedia media) {
        string request = Guid.NewGuid().ToString("N"); files.ReserveReceive(request, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Synqora"));
        if (!server.SendCompanion(new { type = "galleryOriginal", id = media.Id, request })) throw new IOException("The phone disconnected.");
    }
    private void Receive(JsonElement e) {
        try {
            string? changed = null; PhoneMedia[] thumbnailsToRequest = [];
            lock (gate) switch (e.GetProperty("type").GetString()) {
                case "galleryStatus":
                    Enabled = e.GetProperty("enabled").GetBoolean();
                    Message = Enabled ? "Photos and videos shared by your phone" : "In Synqora on your phone, open Files → Photos and choose what to share.";
                    if (!Enabled) { items.Clear(); thumbs.Clear(); } break;
                case "galleryChanged": changed = "refresh"; break;
                case "galleryError": Message = "Photo access changed or this item is unavailable. Refresh or check access on your phone."; break;
                case "galleryPage":
                    items = e.GetProperty("items").EnumerateArray().Take(24).Select(x => new PhoneMedia(x.GetProperty("id").GetString() ?? "", (x.GetProperty("name").GetString() ?? "Photo")[..Math.Min(180, (x.GetProperty("name").GetString() ?? "Photo").Length)], x.GetProperty("size").GetInt64(), x.GetProperty("video").GetBoolean(), x.GetProperty("time").GetInt64())).ToList();
                    Offset = Math.Max(0, e.GetProperty("offset").GetInt32()); More = e.GetProperty("more").GetBoolean(); thumbs.Clear();
                    Message = items.Count == 0 ? "No shared photos or videos on this page." : $"{Offset + 1}–{Offset + items.Count} · originals stay on your phone until you save them";
                    thumbnailsToRequest = items.ToArray();
                    break;
                case "galleryThumb":
                    var id = e.GetProperty("id").GetString() ?? ""; if (!items.Any(i => i.Id == id)) return;
                    var encoded = e.GetProperty("jpeg").GetString() ?? ""; if (encoded.Length > 44000) return;
                    var bytes = Convert.FromBase64String(encoded); if (!Wire.ValidJpeg(bytes)) return; thumbs[id] = bytes; changed = id; break;
                default: return;
            }
            foreach (var item in thumbnailsToRequest) server.SendCompanion(new { type = "galleryThumb", id = item.Id });
            Changed?.Invoke(changed);
            // UI requests pages only while Photos is open; no background photo download.
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException) { }
    }
}
