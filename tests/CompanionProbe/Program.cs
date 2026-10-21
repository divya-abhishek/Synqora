using Synqora.Core;
using System.Text.Json;
using System.Security.Cryptography;

// Disposable emulator receiver. It never routes audio, injects input, or accesses real notifications.
var folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
await using var server = new SessionServer(new Sink());
var files = new FileExchange(server);
var payload = new byte[1_048_737]; new Random(731).NextBytes(payload);
var source = Path.Combine(folder, "pc-to-phone.bin"); File.WriteAllBytes(source, payload);
bool replied = false, actionResult = false, disconnected = false, offered = false;
bool galleryPage = false, galleryThumb = false, originalRequested = false, historySeen = false;
int notifications = 0; long audio = 0, video = 0;
files.Changed += () => { foreach (var file in files.Transfers.Where(f => f.Incoming && f.Status == "Waiting")) files.Accept(file.Id, Path.Combine(folder, "received")); };
server.Changed += state => { if (state.Connected && !offered) { offered = true; _ = Task.Run(async () => { await Task.Delay(500); await files.Offer(source); }); } };
server.Companion += e => {
    switch (e.GetProperty("type").GetString()) {
        case "notification":
            notifications++;
            if (e.GetProperty("title").GetString() == "Synqora reply fixture" && !replied) {
                var action = e.GetProperty("actions").EnumerateArray().FirstOrDefault(a => a.GetProperty("reply").GetBoolean());
                if (action.ValueKind != JsonValueKind.Undefined) { replied = true; server.SendCompanion(new { type = "notificationAction", id = e.GetProperty("id").GetString(), version = e.GetProperty("version").GetString(), action = action.GetProperty("id").GetString(), text = "Synqora integration reply", request = "fixture-reply" }); }
            }
            break;
        case "actionResult": actionResult = e.GetProperty("ok").GetBoolean(); break;
        case "galleryStatus": if (e.GetProperty("enabled").GetBoolean()) server.SendCompanion(new { type = "galleryPage", offset = 0 }); break;
        case "galleryPage":
            var photo = e.GetProperty("items").EnumerateArray().FirstOrDefault(x => x.GetProperty("name").GetString() == "synqora-gallery-fixture.png");
            if (photo.ValueKind != JsonValueKind.Undefined) { galleryPage = true; server.SendCompanion(new { type = "galleryThumb", id = photo.GetProperty("id").GetString() }); if (!originalRequested) { originalRequested = true; string request = Guid.NewGuid().ToString("N"); files.ReserveReceive(request, Path.Combine(folder, "gallery")); server.SendCompanion(new { type = "galleryOriginal", id = photo.GetProperty("id").GetString(), request }); } }
            break;
        case "galleryThumb": var thumb = Convert.FromBase64String(e.GetProperty("jpeg").GetString()!); galleryThumb = Wire.ValidJpeg(thumb); break;
        case "callsStatus": if (e.GetProperty("history").GetBoolean()) server.SendCompanion(new { type = "callsHistory" }); break;
        case "callsHistory": historySeen = e.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("number").GetString() == "5550100"); break;
        case "fixtureDisconnect": _ = Task.Run(() => { audio = server.AudioBytes; video = server.VideoFrames; server.Disconnect(); disconnected = true; }); break;
    }
};
server.Start(loopbackOnly: true);
File.WriteAllText(Path.Combine(folder, "companion-invite.txt"), server.Invitation("127.0.0.1"));
try {
    for (int i = 0; i < 600 && !File.Exists(Path.Combine(folder, "companion-stop")); i++) {
        var result = new { notifications, replied, actionResult, disconnected, audio, video, galleryPage, galleryThumb, historySeen, files = files.Transfers, expectedHash = Convert.ToHexString(SHA256.HashData(payload)) };
        var path = Path.Combine(folder, "companion-result.json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(result)); File.Move(path + ".tmp", path, true); await Task.Delay(500);
    }
} finally { File.Delete(Path.Combine(folder, "companion-invite.txt")); }
sealed class Sink : IInputSink { public void Move(float x, float y) { } public void Click(bool right) { } public void Scroll(float d) { } public void Text(string t) { } public void Key(string k, int m) { } public void Media(string c) { } public void Volume(float l) { } }
