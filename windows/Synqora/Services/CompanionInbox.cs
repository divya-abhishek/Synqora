using System.Text.Json;
using Synqora.Core;

namespace Synqora.Services;
public sealed record PhoneAction(string Id, string Label, bool Reply);
public sealed record PhoneNotification(string Id, string Version, string App, string Title, string Text, long Time, bool Call, IReadOnlyList<PhoneAction> Actions);
public sealed class CompanionInbox
{
    private readonly SessionServer server;
    private readonly object gate = new();
    private readonly Dictionary<string, PhoneNotification> items = [];
    public IReadOnlyList<PhoneNotification> Items { get { lock (gate) return items.Values.OrderByDescending(x => x.Time).ToArray(); } }
    public bool Enabled { get; private set; }
    public string Result { get; private set; } = "";
    public event Action? Changed;
    public CompanionInbox(SessionServer server) {
        this.server = server;
        server.Changed += state => { if (!state.Connected) { lock (gate) { items.Clear(); Enabled = false; Result = ""; } Changed?.Invoke(); } };
        server.Companion += Receive;
    }
    private void Receive(JsonElement e) {
        try {
            lock (gate) switch (e.GetProperty("type").GetString()) {
                case "notification":
                    string Text(string key, int max) => new((e.GetProperty(key).GetString() ?? "").Where(c => !char.IsControl(c) || c is '\n' or '\t').Take(max).ToArray());
                    var id = Text("id", 64);
                    if (items.Count >= 80 && !items.ContainsKey(id)) items.Remove(items.Values.MinBy(x => x.Time)!.Id);
                    items[id] = new(id, Text("version", 64), Text("app", 100), Text("title", 200), Text("text", 3000), e.GetProperty("time").GetInt64(), e.GetProperty("call").GetBoolean(), e.GetProperty("actions").EnumerateArray().Take(4).Select(a => new PhoneAction(a.GetProperty("id").GetString() ?? "", (a.GetProperty("label").GetString() ?? "")[..Math.Min(80, (a.GetProperty("label").GetString() ?? "").Length)], a.GetProperty("reply").GetBoolean())).ToArray());
                    break;
                case "notificationRemove": items.Remove(e.GetProperty("id").GetString() ?? ""); break;
                case "notificationsClear": items.Clear(); Enabled = false; Result = ""; break;
                case "notificationStatus": Enabled = e.GetProperty("enabled").GetBoolean(); break;
                case "actionResult": Result = (e.GetProperty("message").GetString() ?? "")[..Math.Min(300, (e.GetProperty("message").GetString() ?? "").Length)]; break;
                default: return;
            }
            Changed?.Invoke();
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException) { }
    }
    public void Action(PhoneNotification n, PhoneAction action, string text = "") { Result = "Sending to phone…"; if (!server.SendCompanion(new { type = "notificationAction", id = n.Id, version = n.Version, action = action.Id, text, request = Guid.NewGuid().ToString("N") })) Result = "Phone disconnected."; Changed?.Invoke(); }
    public void Dismiss(PhoneNotification n) => server.SendCompanion(new { type = "notificationDismiss", id = n.Id, version = n.Version, request = Guid.NewGuid().ToString("N") });
    public void Dial(string number) { if (!server.SendCompanion(new { type = "dial", number, request = Guid.NewGuid().ToString("N") })) Result = "Connect your phone first."; else Result = "Check the dial request notification on your phone."; Changed?.Invoke(); }
}
