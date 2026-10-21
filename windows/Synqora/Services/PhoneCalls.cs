using System.Text.Json;
using Synqora.Core;
namespace Synqora.Services;
public sealed record CallHistoryItem(string Number, string Name, long Time, long Duration, int Kind);
public sealed class PhoneCalls
{
    private readonly SessionServer server;
    private readonly object gate = new();
    private List<CallHistoryItem> items = [];
    public IReadOnlyList<CallHistoryItem> Items { get { lock (gate) return items.ToArray(); } }
    public bool HistoryEnabled { get; private set; }
    public bool CanDial { get; private set; }
    public string Message { get; private set; } = "Enable call sharing on your phone in Inbox → Call settings.";
    public event Action<bool>? Changed;
    public PhoneCalls(SessionServer server) {
        this.server = server; server.Companion += Receive;
        server.Changed += s => { if (!s.Connected) { lock (gate) { items.Clear(); CanDial = false; HistoryEnabled = false; Message = "Connect your phone to view recent calls."; } Changed?.Invoke(false); } };
    }
    public void Refresh() => server.SendCompanion(new { type = "callsHistory" });
    public void Place(string number) {
        if (!CanDial) throw new InvalidOperationException("Allow PC calling in Synqora on your phone first.");
        if (number.Length is < 1 or > 40 || !number.Any(char.IsDigit) || number.Any(c => !"+0123456789 ()-".Contains(c))) throw new ArgumentException("Enter a valid phone number.");
        if (!server.SendCompanion(new { type = "callPlace", number, request = Guid.NewGuid().ToString("N") })) throw new IOException("The phone disconnected.");
    }
    private void Receive(JsonElement e) {
        try {
            bool refresh = false;
            lock (gate) switch (e.GetProperty("type").GetString()) {
                case "callsStatus":
                    bool before = HistoryEnabled; HistoryEnabled = e.GetProperty("history").GetBoolean(); CanDial = e.GetProperty("dial").GetBoolean();
                    if (!HistoryEnabled) items.Clear(); refresh = HistoryEnabled && !before;
                    Message = HistoryEnabled || CanDial ? "Call access enabled on your phone" : "Enable call sharing on your phone in Inbox → Call settings."; break;
                case "callsChanged": refresh = true; break;
                case "callsHistory":
                    items = e.GetProperty("items").EnumerateArray().Take(50).Select(x => new CallHistoryItem((x.GetProperty("number").GetString() ?? "").Replace("\r", "").Replace("\n", ""), (x.GetProperty("name").GetString() ?? "").Replace("\r", "").Replace("\n", ""), Math.Clamp(x.GetProperty("time").GetInt64(), 0, 253402300799999L), Math.Max(0, x.GetProperty("duration").GetInt64()), x.GetProperty("kind").GetInt32())).ToList(); break;
                default: return;
            }
            Changed?.Invoke(refresh);
        } catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
    }
}
