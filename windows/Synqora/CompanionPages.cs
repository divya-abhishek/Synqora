using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Synqora.Services;
using static Synqora.Design.Glass;

namespace Synqora;
public sealed partial class MainWindow
{
    private GridView? photos;
    private TextBlock? photoStatus;
    private readonly Dictionary<string, Image> photoImages = [];
    private readonly HashSet<string> loadedPhotos = [];
    private bool galleryWasEnabled, galleryDirty;
    private int photoGeneration;
    private StackPanel? recentCalls;
    private TextBox? dialNumber;
    private Button? dialButton;
    private TextBlock? callAccess, dialResult;
    private StackPanel? bluetoothCallList;
    private TextBlock? bluetoothStatus;
    private StackPanel CallsPage() {
        dialNumber = new TextBox { Header = "Phone number", PlaceholderText = "Enter a phone number", MaxLength = 40, CornerRadius = new(12) };
        dialResult = g.Text("", 13, "muted");
        dialButton = g.Button("Call", async () => { try { if (bluetooth.CanDial) { await bluetooth.Dial(dialNumber.Text.Trim()); dialResult.Text = "Call requested over Bluetooth."; } else { calls.Place(dialNumber.Text.Trim()); dialResult.Text = "Call requested. Audio uses your phone's configured route."; } } catch (Exception error) { dialResult.Text = error.Message; } }, true, "\uE717");
        dialButton.IsEnabled = calls.CanDial || bluetooth.CanDial;
        callAccess = g.Text(calls.Message, 13, "muted"); recentCalls = Stack(10); callList = Stack(10);
        bluetoothCallList = Stack(10); bluetoothStatus = g.Text(bluetooth.Message, 13, "muted");
        var connectBluetooth = g.Button("Connect Bluetooth calling", ConnectBluetooth, glyph: "\uE702"); connectBluetooth.IsEnabled = bluetooth.CanUse;
        RefreshCallHistory(); RefreshNotifications(); RefreshBluetoothCalls(); calls.Refresh();
        return Stack(24, Heading("", "Calls", "Recent calls and dialing."),
            g.Section(Stack(14, dialNumber, dialButton, dialResult, callAccess,
                g.Text("With Bluetooth calling connected, calls use your PC microphone and speakers. Otherwise direct dialing uses your phone's configured audio route.", 13, "muted"))),
            g.Section(Stack(12, g.Text("Bluetooth calling · experimental", 18, "text", true), bluetoothStatus, connectBluetooth, bluetoothCallList)),
            g.Between(g.Text("Recent calls", 18, "text", true), g.Button("Refresh", calls.Refresh)), recentCalls,
            g.Rule(), g.Text("WhatsApp and other call notifications", 18, "text", true),
            g.Text("Available answer and decline actions appear below when the calling app provides them. WhatsApp's private call history and direct calling are not exposed to Synqora.", 13, "muted"),
            Live(() => inbox.Result, 13, "muted"), callList);
    }
    private void RefreshCallHistory() {
        if (dialButton != null) dialButton.IsEnabled = calls.CanDial || bluetooth.CanDial;
        if (callAccess != null) callAccess.Text = calls.Message;
        if (recentCalls == null) return;
        recentCalls.Children.Clear();
        foreach (var entry in calls.Items) {
            string kind = entry.Kind switch { 1 => "Incoming", 2 => "Outgoing", 3 => "Missed", 5 => "Declined", _ => "Call" };
            var button = g.Button(string.IsNullOrWhiteSpace(entry.Name) ? entry.Number : entry.Name, () => { if (dialNumber != null) { dialNumber.Text = entry.Number; scroll.ChangeView(null, 0, null, true); dialNumber.Focus(FocusState.Programmatic); } });
            button.Content = Stack(5, g.Text(string.IsNullOrWhiteSpace(entry.Name) ? entry.Number : entry.Name, 16, "text", true), g.Text($"{kind} · {DateTimeOffset.FromUnixTimeMilliseconds(entry.Time).ToLocalTime():g}" + (string.IsNullOrWhiteSpace(entry.Name) ? "" : $" · {entry.Number}"), 12, "muted"));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; recentCalls.Children.Add(button);
        }
        if (recentCalls.Children.Count == 0) recentCalls.Children.Add(g.Text(calls.HistoryEnabled ? "No recent calls in the phone's call log." : "Call history needs permission on your phone.", 14, "muted"));
    }
    private async void ConnectBluetooth() {
        try {
            var phones = await bluetooth.Discover();
            var list = Stack(12, g.Text("Pair your phone in Windows Bluetooth settings first. Then choose it below. Windows may ask for calling access.", 14, "muted"));
            if (phones.Count == 0) list.Children.Add(g.Text("No paired Bluetooth phone is available.", 14, "muted"));
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Bluetooth calling", Content = list, CloseButtonText = "Cancel" };
            foreach (var phone in phones) list.Children.Add(g.Button(phone.Name, async () => { dialog.Hide(); try { await bluetooth.Connect(phone.Id); } catch (Exception error) { if (bluetoothStatus != null) bluetoothStatus.Text = error.Message; } }));
            await dialog.ShowAsync();
        } catch (Exception error) { if (bluetoothStatus != null) bluetoothStatus.Text = error.Message; }
    }
    private void RefreshBluetoothCalls() {
        if (bluetoothStatus != null) bluetoothStatus.Text = bluetooth.Message;
        if (dialButton != null) dialButton.IsEnabled = calls.CanDial || bluetooth.CanDial;
        if (bluetoothCallList == null) return;
        bluetoothCallList.Children.Clear();
        foreach (var call in bluetooth.Active) {
            var buttons = Stack(8);
            foreach (var (label, action) in new[] { ("Answer", "answer"), ("Decline", "decline"), ("End call", "end"), (call.Muted ? "Unmute" : "Mute", "mute"), (call.LocalAudio ? "Use phone audio" : "Use PC audio", "audio") }) {
                bool incoming = call.State == "Ringing" || call.State == "Incoming";
                if (action is "answer" or "decline" && !incoming || action is "end" or "mute" or "audio" && incoming) continue;
                buttons.Children.Add(g.Button(label, async () => { try { await bluetooth.Control(call.Id, action); } catch (Exception error) { if (bluetoothStatus != null) bluetoothStatus.Text = error.Message; } }));
            }
            bluetoothCallList.Children.Add(g.Surface(Stack(12, g.Text(string.IsNullOrWhiteSpace(call.Name) ? call.Number : call.Name, 17, "text", true), g.Text($"{call.State} · {(call.LocalAudio ? "PC audio" : "Phone audio")}", 13, "muted"), buttons), "Subtle", 16));
        }
    }
    private StackPanel PhotosPage() {
        photos = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, IsTabStop = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        photos.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' ItemWidth='180' ItemHeight='240'/></ItemsPanelTemplate>");
        photoStatus = g.Text(gallery.Message, 14, "muted");
        GalleryChanged(null); gallery.Page();
        return Stack(24, Heading("", "Photos", "Photos and videos from your phone."),
            g.Section(Stack(12, g.Text("Share your gallery", 19, "text", true), g.Text("On your phone, open Files → Photos to choose photo access. Thumbnails load as you browse. Original files are transferred only when you choose Save original.", 14, "muted"))),
            Row(10, g.Button("Refresh", () => gallery.Page()), g.Button("Previous", () => gallery.Page(Math.Max(0, gallery.Offset - 24))), g.Button("Next", () => { if (gallery.More) gallery.Page(gallery.Offset + 24); })), photoStatus, photos);
    }
    private void GalleryChanged(string? id) {
        if (id == "refresh") { galleryDirty = true; if (page == "Photos") { galleryDirty = false; gallery.Page(); } return; }
        if (gallery.Enabled && !galleryWasEnabled && page == "Photos") gallery.Page(); galleryWasEnabled = gallery.Enabled;
        if (photos == null || photoStatus == null) return;
        photoStatus.Text = gallery.Message;
        if (id != null) { _ = LoadThumbnail(id, photoGeneration); return; }
        photoGeneration++; loadedPhotos.Clear(); photoImages.Clear(); photos.Items.Clear();
        foreach (var media in gallery.Items) {
            var image = new Image { Height = 132, Width = 148, Stretch = Stretch.UniformToFill };
            photoImages[media.Id] = image;
            var name = g.Text(media.Name, 12, "muted"); name.MaxLines = 1; name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxWidth = 148;
            var save = g.Button("Save original", () => { try { gallery.Original(media); Navigate("Files"); } catch (Exception error) { photoStatus.Text = error.Message; } }); save.Padding = new(12, 8, 12, 8);
            photos.Items.Add(g.Surface(Stack(8, image, name, save), "Subtle", 12));
            _ = LoadThumbnail(media.Id, photoGeneration);
        }
    }
    private async Task LoadThumbnail(string id, int generation) {
        if (!photoImages.TryGetValue(id, out var image) || loadedPhotos.Contains(id) || gallery.Thumbnail(id) is not byte[] bytes) return;
        loadedPhotos.Add(id);
        try {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream(); await stream.WriteAsync(bytes.AsBuffer()); stream.Seek(0);
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (generation == photoGeneration) image.Source = bitmap;
        } catch (Exception error) when (error is IOException or System.Runtime.InteropServices.COMException) { loadedPhotos.Remove(id); }
    }
}
