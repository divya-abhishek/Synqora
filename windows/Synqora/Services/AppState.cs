using System.Text.Json;

namespace Synqora.Services;

public enum ConnectionPhase { Disconnected, Connecting, Connected, Error }
public sealed record DeviceSnapshot(string Name, string Identity, ConnectionPhase Phase, double? LatencyMs = null, int? Battery = null);

// Media/transport adapters publish immutable snapshots. UI never owns an audio callback.
public interface ISynqoraSession
{
    DeviceSnapshot Device { get; }
    bool CanStream { get; }
    event Action<DeviceSnapshot>? Changed;
    Task DisconnectAsync();
}

public sealed class UnconfiguredSession : ISynqoraSession
{
    public DeviceSnapshot Device => new("Your next connection", "No device paired", ConnectionPhase.Disconnected);
    public bool CanStream => false;
    public event Action<DeviceSnapshot>? Changed { add { } remove { } }
    public Task DisconnectAsync() => Task.CompletedTask;
}

public sealed class Preferences
{
    private static readonly object SaveGate = new();
    private CancellationTokenSource? pendingSave;
    public string Theme { get; set; } = "System";
    public bool ReducedMotion { get; set; }
    public bool ReducedTransparency { get; set; }
    public string Effects { get; set; } = "Balanced";
    public double Gain { get; set; } = 70;
    public string AudioQuality { get; set; } = "Studio · 48 kHz";
    public bool Muted { get; set; }
    public bool CameraFill { get; set; }
    public double Volume { get; set; } = 50;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synqora", "preferences.json");
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        pendingSave?.Cancel();
        pendingSave?.Dispose(); pendingSave = null;
        WriteSnapshot(JsonSerializer.Serialize(this));
    }
    // Control input is immediate; coalesce only the preference file write.
    public void SaveSoon()
    {
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        var cancellation = new CancellationTokenSource(); pendingSave = cancellation;
        var token = cancellation.Token;
        var snapshot = JsonSerializer.Serialize(this);
        _ = Task.Run(async () => {
            try { await Task.Delay(300, token); WriteSnapshot(snapshot, token); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StartupDiagnostics.Write("Save preferences", error); }
        });
    }
    private static void WriteSnapshot(string snapshot, CancellationToken token = default)
    {
        lock (SaveGate) {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, snapshot);
            File.Move(temp, FilePath, true);
        }
    }
}
