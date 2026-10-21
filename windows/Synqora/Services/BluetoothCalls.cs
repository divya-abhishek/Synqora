using System.Collections.Concurrent;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace Synqora.Services;
public sealed record BluetoothPhone(string Id, string Name);
public sealed record BluetoothCallView(string Id, string Name, string Number, string State, bool Muted, bool LocalAudio);

/// <summary>Real Windows HFP calling. Requires package identity and phoneLineTransportManagement.</summary>
public sealed class BluetoothCalls : IDisposable
{
    private PhoneLineTransportDevice? transport;
    private PhoneLineWatcher? watcher;
    private readonly ConcurrentDictionary<Guid, PhoneLine> lines = new();
    private readonly ConcurrentDictionary<string, PhoneCall> calls = new();
    private readonly ConcurrentDictionary<string, BluetoothCallView> views = new();
    private readonly SemaphoreSlim refresh = new(1);
    private bool disposed;
    public event Action? Changed;
    public string Message { get; private set; } = HasIdentity ? "Pair your phone in Windows Bluetooth settings, then connect it here." : "Bluetooth calling requires the experimental MSIX build. This MSI/portable build uses phone audio.";
    public static bool HasIdentity { get { try { _ = Package.Current.Id; return true; } catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; } } }
    public bool CanUse => HasIdentity && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
    public bool CanDial => CanUse && lines.Values.Any(l => l.CanDial);
    public IReadOnlyList<BluetoothCallView> Active => views.Values.Where(v => v.State is not ("Ended" or "Lost")).ToArray();
    public async Task<IReadOnlyList<BluetoothPhone>> Discover() {
        if (!CanUse) return [];
        var devices = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
        return devices.Select(d => new BluetoothPhone(d.Id, d.Name)).ToArray();
    }
    public async Task Connect(string id) {
        if (!CanUse) throw new InvalidOperationException(Message);
        Stop(); transport = PhoneLineTransportDevice.FromId(id);
        var access = await transport.RequestAccessAsync();
        if (access != DeviceAccessStatus.Allowed) throw new InvalidOperationException($"Windows did not allow Bluetooth calling ({access}). See the calling setup guide.");
        transport.RegisterApp();
        if (!await transport.ConnectAsync()) throw new IOException("Windows could not connect to the phone's hands-free service.");
        var store = await PhoneCallManager.RequestStoreAsync(); watcher = store.RequestLineWatcher();
        watcher.LineAdded += LineAdded;
        watcher.LineRemoved += (sender, args) => { if (lines.TryRemove(args.LineId, out var line)) line.LineChanged -= LineChanged; _ = Refresh(); };
        watcher.EnumerationCompleted += (_, _) => { Message = lines.Count == 0 ? "Connected to Bluetooth, but Windows has not exposed a calling line." : "Bluetooth phone ready · PC audio available"; Changed?.Invoke(); };
        watcher.Start(); Message = "Connecting Bluetooth calling…"; Changed?.Invoke();
    }
    private async void LineAdded(PhoneLineWatcher sender, PhoneLineWatcherEventArgs args) {
        try {
            var connectedTransport = transport;
            var line = await PhoneLine.FromIdAsync(args.LineId);
            if (disposed || sender != watcher || connectedTransport == null || !ReferenceEquals(connectedTransport, transport) || line.Transport != PhoneLineTransport.Bluetooth || line.TransportDeviceId != connectedTransport.DeviceId) return;
            lines[line.Id] = line; line.LineChanged += LineChanged;
            Message = "Bluetooth phone ready · PC audio available"; Changed?.Invoke(); await Refresh();
        } catch (Exception error) { Fail(error); }
    }
    private void LineChanged(PhoneLine line, object args) => _ = Refresh();
    public async Task Refresh() {
        await refresh.WaitAsync();
        try {
            var connectedTransport = transport;
            if (disposed || connectedTransport == null) return;
            var live = new HashSet<string>();
            foreach (var line in lines.Values) {
                var result = await line.GetAllActivePhoneCallsAsync();
                if (disposed || !ReferenceEquals(connectedTransport, transport)) return;
                foreach (var call in result.AllActivePhoneCalls) {
                    live.Add(call.CallId);
                    if (calls.TryAdd(call.CallId, call)) { call.StatusChanged += CallChanged; call.AudioDeviceChanged += CallChanged; call.IsMutedChanged += CallChanged; }
                    var info = await call.GetPhoneCallInfoAsync();
                    if (disposed || !ReferenceEquals(connectedTransport, transport)) return;
                    views[call.CallId] = new(call.CallId, info.DisplayName, info.PhoneNumber, call.Status.ToString(), call.IsMuted, call.AudioDevice == PhoneCallAudioDevice.LocalDevice);
                }
            }
            foreach (var id in calls.Keys.Where(k => !live.Contains(k)).ToArray()) RemoveCall(id);
            Changed?.Invoke();
        } catch (Exception error) { Fail(error); }
        finally { refresh.Release(); }
    }
    private void CallChanged(PhoneCall call, object args) => _ = Refresh();
    public async Task Dial(string number) {
        if (number.Length is < 1 or > 40 || !number.Any(char.IsDigit) || number.Any(c => !"+0123456789 ()-".Contains(c))) throw new ArgumentException("Enter a valid phone number.");
        var line = lines.Values.FirstOrDefault(l => l.CanDial) ?? throw new InvalidOperationException("Connect Bluetooth calling first.");
        var result = line.DialWithResult(number, number);
        if (result.DialedCall == null) throw new IOException($"Windows could not place the call ({result.DialCallStatus}).");
        await result.DialedCall.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); await Refresh();
    }
    public async Task Control(string id, string action) {
        if (!calls.TryGetValue(id, out var call)) throw new IOException("This call has ended.");
        switch (action) {
            case "answer": await call.AcceptIncomingAsync(); await call.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); break;
            case "decline": await call.RejectIncomingAsync(); break;
            case "end": await call.EndAsync(); break;
            case "mute": if (call.IsMuted) await call.UnmuteAsync(); else await call.MuteAsync(); break;
            case "audio": await call.ChangeAudioDeviceAsync(call.AudioDevice == PhoneCallAudioDevice.LocalDevice ? PhoneCallAudioDevice.RemoteDevice : PhoneCallAudioDevice.LocalDevice); break;
        }
        await Refresh();
    }
    private void RemoveCall(string id) { if (calls.TryRemove(id, out var call)) { call.StatusChanged -= CallChanged; call.AudioDeviceChanged -= CallChanged; call.IsMutedChanged -= CallChanged; } views.TryRemove(id, out _); }
    private void Fail(Exception error) { Message = "Bluetooth calling: " + error.Message; Changed?.Invoke(); }
    public void Stop() {
        watcher?.Stop(); watcher = null;
        foreach (var line in lines.Values) line.LineChanged -= LineChanged; lines.Clear(); foreach (var id in calls.Keys) RemoveCall(id);
        try { if (transport?.IsRegistered() == true) transport.UnregisterApp(); } catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        transport = null;
    }
    public void Dispose() { disposed = true; Stop(); }
}
