using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Path = System.IO.Path;
using Synqora.Design;
using Synqora.Services;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Channels;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.ViewManagement;
using static Synqora.Design.Glass;

namespace Synqora;

public sealed class MainWindow : Window
{
    private readonly Preferences preferences = Preferences.Load();
    private readonly WindowsSession session = new();
    private readonly List<Action> liveBindings = [];
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource mediaCancellation = new();
    private readonly Channel<(byte[] Jpeg, long Generation, int Rotation)> jpegFrames = Channel.CreateBounded<(byte[], long, int)>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private GlassAudioVisualizer? audioView;
    private Image? cameraImage;
    private string videoDimensions = "—";
    private long decodedFrames;
    private double decodedFps;
    private long rateStarted, rateFrames;
    private int presentationPending;
    private readonly CameraBridge? cameraBridge;
    private string cameraBridgeError = "";
    private bool cameraPublishing;
    private bool cameraAttempted;
    private readonly Glass g;
    private Grid root = null!, shell = null!;
    private StackPanel nav = null!;
    private Border navSelection = null!;
    private ScrollViewer scroll = null!;
    private readonly string[] pages = ["Overview", "Microphone", "Webcam", "Touchpad", "Keyboard", "Settings"];
    private readonly string[] glyphs = ["\uE80F", "\uE720", "\uE714", "\uE7C9", "\uE765", "\uE713"];
    private string page = "Overview";
    private readonly Dictionary<string, double> scrollPositions = [];
    private readonly Dictionary<string, StackPanel> pageCache = [];
    private readonly Dictionary<string, Button> navButtons = [];
    private string mediaError = "";
    private readonly UISettings system = new();
    private readonly AccessibilitySettings accessibility = new();
    private readonly string[] args;
    private bool capturing;
    private bool compact;
    private bool lastHighContrast;

    public MainWindow(string[] arguments)
    {
        args = arguments; g = new Glass(preferences);
        try { cameraBridge = new CameraBridge(); cameraBridge.Framing(preferences.CameraFill); }
        catch (Exception error) { cameraBridgeError = error.Message; }
        session.Gain = (float)preferences.Gain / 100;
        session.Muted = preferences.Muted;
        Title = "Synqora";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "synqora.ico"));
        int ArgInt(string key, int fallback) { var at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback; }
        AppWindow.Resize(new SizeInt32(Math.Max(700, ArgInt("--width", 940)), Math.Max(600, ArgInt("--height", 760))));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 700;
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        Build();
        session.Changed += _ => DispatcherQueue.TryEnqueue(() => {
            foreach (var update in liveBindings.ToArray()) update();
            audioView?.SetStreaming(session.Server.State.Microphone && page == "Microphone");
            if (session.CanStream) liveTimer.Start(); else { liveTimer.Stop(); videoDimensions = "—"; if (cameraImage != null) cameraImage.Source = null; }
        });
        liveTimer.Tick += (_, _) => { foreach (var update in liveBindings.ToArray()) update(); };
        session.Levels += values => audioView?.Publish(values);
        session.Video += (bytes, rotation) => jpegFrames.Writer.TryWrite((bytes, cameraBridge?.Generation ?? 0, rotation));
        session.Server.Changed += state => {
            cameraBridge?.Streaming(state.Connected && state.Camera);
            if (!state.Connected || !state.Camera) cameraAttempted = false;
            else if (!cameraAttempted && !args.Contains("--receiver-test")) {
                cameraAttempted = true; DispatcherQueue.TryEnqueue(() => _ = PublishCamera(true));
            }
        };
        _ = Task.Run(DecodeVideo);
        Closed += (_, _) => { liveTimer.Stop(); preferences.Save(); mediaCancellation.Cancel(); jpegFrames.Writer.TryComplete(); session.Dispose(); _ = Task.Run(() => cameraBridge?.Dispose()); };
        system.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (preferences.Theme == "System") Build(); });
        system.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        system.AnimationsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        Windows.System.Power.PowerManager.EnergySaverStatusChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        try { accessibility.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(Build); }
        catch (System.Runtime.InteropServices.COMException) { /* Some unpackaged environments do not expose this event. Refresh on activation below. */ }
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated && lastHighContrast != g.HighContrast) Build(); };
    }
    private void Build()
    {
        liveBindings.Clear(); audioView = null; cameraImage = null;
        if (scroll is not null) scrollPositions[page] = scroll.VerticalOffset;
        pageCache.Clear();
        lastHighContrast = g.HighContrast;
        g.Dark = preferences.Theme == "Dark" || (preferences.Theme == "System" && system.GetColorValue(UIColorType.Background).R < 128);
        SystemBackdrop = !g.Solid && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : !g.Solid && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() ? new MicaBackdrop() : null;
        root = new Grid { Background = g.Brush("background", g.Solid ? 1 : .16), RequestedTheme = g.Dark ? ElementTheme.Dark : ElementTheme.Light };

        root.Resources["SystemAccentColor"] = g.Color("accent");
        root.Resources["SystemControlHighlightAccentBrush"] = g.Brush("accent");
        root.Resources["SliderTrackValueFill"] = g.Brush("accent");
        root.Resources["SliderThumbBackground"] = g.Brush("accent");
        root.Resources["SliderThumbBackgroundPointerOver"] = g.Brush("accent");
        root.Resources["SliderThumbBackgroundPressed"] = g.Brush("accent", .85);
        root.Resources["AccentFillColorDefaultBrush"] = g.Brush("accent");
        root.Resources["AccentFillColorSecondaryBrush"] = g.Brush("accent", .9);
        root.Resources["AccentFillColorTertiaryBrush"] = g.Brush("accent", .8);
        root.Resources["TextOnAccentFillColorPrimaryBrush"] = g.Brush("onAccent");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = Row(8, Brand.Mark(g), g.Text("Synqora", 15, "text", true));
        title.Margin = new(24, 0, 150, 0); title.Background = g.Brush("background", .01);
        root.Children.Add(title); SetTitleBar(title);
        AppWindow.TitleBar.ButtonForegroundColor = g.Color("text");
        shell = new Grid { MaxWidth = 1000, Margin = new(16, 0, 16, 16) };
        shell.RowDefinitions.Add(new() { Height = GridLength.Auto }); shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(shell, 1); root.Children.Add(shell);
        nav = Row(4); nav.HorizontalAlignment = HorizontalAlignment.Center;
        navSelection = new Border { Height = 46, Width = 80, CornerRadius = new(22), Background = g.Brush(g.HighContrast ? "background" : "accent", .14), BorderBrush = g.Brush("text", g.HighContrast ? 1 : .16), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var navLayer = new Grid(); navLayer.Children.Add(navSelection); navLayer.Children.Add(nav); BuildNav();
        nav.Children.Add(g.IconButton("\uE8D4", "Connection details", ConnectionDetails));
        var navigation = g.Surface(navLayer, "Elevated", 6); navigation.HorizontalAlignment = HorizontalAlignment.Center; navigation.CornerRadius = new(28); navigation.Margin = new(0, 8, 0, 16); shell.Children.Add(navigation);
        nav.SizeChanged += (_, _) => { if (navButtons.TryGetValue(page, out var selected)) g.MoveSelection(navSelection, selected, nav, false); };
        scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(16, 8, 16, 24) }; Grid.SetRow(scroll, 1); shell.Children.Add(scroll);
        root.SizeChanged += (_, e) => { var next = e.NewSize.Width < 850; if (compact != next) { compact = next; DispatcherQueue.TryEnqueue(Build); } };
        Content = root; RenderPage(false);
        root.Loaded += async (_, _) => {
            if (capturing) return;
            if (args.Contains("--capture")) { capturing = true; await CapturePreviews(); }
            else if (args.Contains("--receiver-test")) { capturing = true; await ReceiverTest(); }
        };
    }
    private void BuildNav()
    {
        nav.Children.Clear();
        navButtons.Clear();
        for (int i = 0; i < pages.Length; i++)
        {
            var dest = pages[i]; var selected = dest == page;
            var b = g.Button(dest == "Overview" ? "Home" : dest, () => Navigate(dest), glyph: glyphs[i]);
            b.HorizontalAlignment = HorizontalAlignment.Left; b.HorizontalContentAlignment = HorizontalAlignment.Center; b.MinWidth = compact ? 52 : 104; b.Padding = new(compact ? 12 : 14, 12, compact ? 12 : 14, 12);
            if (compact) { var icon = g.Icon(glyphs[i]); icon.HorizontalAlignment = HorizontalAlignment.Center; b.Content = icon; b.HorizontalContentAlignment = HorizontalAlignment.Center; ToolTipService.SetToolTip(b, dest); }
            b.Background = g.Brush("surface", 0);
            b.BorderBrush = g.Brush("border", 0); b.CornerRadius = new(22);
            AutomationProperties.SetAutomationId(b, "nav-" + dest);
            AutomationProperties.SetItemStatus(b, selected ? "Current page" : "");
            b.AccessKey = (i + 1).ToString();
            navButtons[dest] = b;
            nav.Children.Add(b);
        }
    }
    private void Navigate(string dest)
    {
        if (page == dest) return;
        // SoftwareBitmapSource owns native video resources; do not retain a source across detach.
        if (cameraImage != null) cameraImage.Source = null;
        scrollPositions[page] = scroll.VerticalOffset; page = dest;
        foreach (var (name, button) in navButtons)
        {
            var selected = name == dest;
            AutomationProperties.SetItemStatus(button, selected ? "Current page" : "");
        }
        if (navButtons.TryGetValue(dest, out var selectedButton)) g.MoveSelection(navSelection, selectedButton, nav, false);
        RenderPage(false);
    }
    private void RenderPage(bool animate = false)
    {
        if (!pageCache.TryGetValue(page, out var content))
        {
            content = page switch { "Overview" => Dashboard(), "Microphone" => Microphone(), "Webcam" => Webcam(), "Touchpad" => Touchpad(), "Keyboard" => Keyboard(), _ => Settings() };
            pageCache[page] = content;
        }
        content.MaxWidth = 900; content.HorizontalAlignment = HorizontalAlignment.Stretch;
        scroll.Content = content;
        audioView?.SetStreaming(page == "Microphone" && session.Server.State.Microphone);
        var destination = page;
        DispatcherQueue.TryEnqueue(() => { if (page == destination) scroll.ChangeView(null, scrollPositions.GetValueOrDefault(destination), null, true); });
        if (animate) g.Enter(content);
    }
    private StackPanel Heading(string eyebrow, string title, string subtitle) => Stack(6, g.Display(title, compact ? 28 : 32), g.Text(subtitle, 14, "muted"));
    private async void ConnectionDetails()
    {
        try { session.Start(); }
        catch (Exception error) { await g.Dialog(root.XamlRoot, "Could not start connection", g.Text(error.Message)); return; }
        var invitation = session.Server.Invitation();
        using var qrData = QRCoder.QRCodeGenerator.GenerateQrCode(invitation, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCoder.PngByteQRCode(qrData);
        using var qrStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await qrStream.WriteAsync(qr.GetGraphic(6).AsBuffer()); qrStream.Seek(0);
        var qrImage = new BitmapImage(); await qrImage.SetSourceAsync(qrStream);
        var qrPanel = new Border { Background = new SolidColorBrush(Colors.White), Padding = new(8), CornerRadius = new(20), HorizontalAlignment = HorizontalAlignment.Center, Child = new Image { Source = qrImage, Width = 248, Height = 248 } };
        AutomationProperties.SetName(qrPanel, "Pairing QR code. The same invitation is available in the pairing link field.");
        var link = new TextBox { Header = "Pairing link", Text = invitation, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, CornerRadius = new(14) };
        var copy = g.Button("Copy pairing link", () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(invitation); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }, true, "\uE8C8");
        var content = Stack(16, LiveStatus(() => session.CanStream ? $"Connected to {session.Device.Name}" : "Waiting for your phone"), g.Text("On your phone, tap Connection details → Scan PC code. Keep both devices on the same local network. This invitation expires after ten minutes.", 14, "muted"), qrPanel, link, copy, ControlSwitch(), g.Button("Disconnect phone", () => _ = session.DisconnectAsync()));
        await g.Dialog(root.XamlRoot, "Connect your phone", content);
    }
    private TextBlock Live(Func<string> value, double size = 14, string token = "text") { var label = g.Text(value(), size, token); liveBindings.Add(() => label.Text = value()); return label; }
    private Button LiveButton(Func<string> value, Action action, bool primary = false) {
        var button = g.Button(value(), action, primary); var label = g.Text(value(), 14, primary ? "onAccent" : "text", true); button.Content = label;
        liveBindings.Add(() => { var next = value(); if (label.Text != next) { label.Text = next; AutomationProperties.SetName(button, next); } }); return button;
    }
    private Border LiveStatus(Func<string> value) { var panel = g.Surface(Live(value, 12, "muted"), "Subtle", 10); panel.CornerRadius = new(18); panel.HorizontalAlignment = HorizontalAlignment.Left; return panel; }
    private ToggleSwitch ControlSwitch() { var toggle = g.Switch("Allow phone keyboard & pointer", session.Server.State.ControlsAllowed, session.Server.AllowControls); liveBindings.Add(() => { if (toggle.IsOn != session.Server.State.ControlsAllowed) toggle.IsOn = session.Server.State.ControlsAllowed; }); return toggle; }
    private async Task DecodeVideo()
    {
        try {
            await foreach (var frame in jpegFrames.Reader.ReadAllAsync(mediaCancellation.Token)) {
              try {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(frame.Jpeg.AsBuffer()); stream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var transform = new BitmapTransform { Rotation = frame.Rotation switch { 90 => BitmapRotation.Clockwise90Degrees, 180 => BitmapRotation.Clockwise180Degrees, 270 => BitmapRotation.Clockwise270Degrees, _ => BitmapRotation.None } };
                var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                var dimensions = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";
                Interlocked.Increment(ref decodedFrames);
                var now = Environment.TickCount64;
                if (rateStarted == 0) rateStarted = now;
                rateFrames++;
                if (now - rateStarted >= 1000) { decodedFps = rateFrames * 1000.0 / (now - rateStarted); rateFrames = 0; rateStarted = now; }
                // The virtual device producer never waits for XAML or a minimized preview surface.
                if (session.Server.State.Camera && cameraBridge != null) {
                    try { cameraBridge.Write(bitmap, frame.Generation); } catch (Exception error) { cameraBridgeError = error.Message; }
                }
                if (Interlocked.CompareExchange(ref presentationPending, 1, 0) != 0) { bitmap.Dispose(); continue; }
                if (!DispatcherQueue.TryEnqueue(async () => {
                    try {
                        if (!session.Server.State.Camera || frame.Generation != (cameraBridge?.Generation ?? 0)) return;
                        videoDimensions = dimensions;
                        var target = cameraImage;
                        if (target != null && page == "Webcam" && target.IsLoaded && (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State != OverlappedPresenterState.Minimized)) {
                            var source = target.Source as SoftwareBitmapSource ?? new SoftwareBitmapSource(); await source.SetBitmapAsync(bitmap);
                            if (page == "Webcam" && target.IsLoaded && ReferenceEquals(cameraImage, target) && session.Server.State.Camera && frame.Generation == (cameraBridge?.Generation ?? 0)) target.Source = source;
                        }
                    }
                    catch (Exception e) { mediaError = "Could not display this camera frame: " + e.Message; }
                    finally { bitmap.Dispose(); Interlocked.Exchange(ref presentationPending, 0); }
                })) { bitmap.Dispose(); Interlocked.Exchange(ref presentationPending, 0); }
              } catch (Exception e) when (e is not OperationCanceledException) { DispatcherQueue.TryEnqueue(() => mediaError = "A camera frame could not be decoded."); }
            }
        } catch (OperationCanceledException) { }
    }
    private StackPanel Dashboard()
    {
        var details = Stack(6, g.Text("THIS COMPUTER", 10, "muted", true), g.Display(Environment.MachineName, 27), Live(() => session.CanStream ? $"Connected to {session.Device.Name}" : "Ready for your phone", 14, "muted"));
        var device = new Grid { ColumnSpacing = 20 };
        device.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); device.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); device.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var deviceIcon = g.Surface(g.Icon("\uE7F4", 30), "Elevated", 16); deviceIcon.VerticalAlignment = VerticalAlignment.Center; deviceIcon.Height = deviceIcon.Width = 66; device.Children.Add(deviceIcon); Grid.SetColumn(details, 1); device.Children.Add(details);
        var connect = LiveButton(() => session.CanStream ? "Device details" : "Connect phone", ConnectionDetails, true); Grid.SetColumn(connect, 2); device.Children.Add(connect);
        var connection = g.Surface(Stack(12, device, Row(16, LiveStatus(() => session.CanStream ? "Connected" : "Not connected"), Live(() => session.CanStream ? $"Encrypted · {session.Device.LatencyMs?.ToString("0") ?? "—"} ms" : "Scan the code on this PC to pair", 12, "muted"))), "Regular", 20);
        var features = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (int c = 0; c < 2; c++) features.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int r = 0; r < 2; r++) features.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string[] names = ["Microphone", "Webcam", "Touchpad", "Keyboard"];
        string[] descriptions = ["Phone audio in your apps", "Your phone as a webcam", "Move, tap and scroll", "Type directly on this PC"];
        for (int i = 0; i < names.Length; i++) {
            var name = names[i]; var tile = g.Button(name, () => Navigate(name));
            var symbol = g.Surface(g.Icon(glyphs[i + 1], 23), "Elevated", 12); symbol.CornerRadius = new(18);
            var text = Stack(4, g.Text(name, 17, "text", true), g.Text(descriptions[i], 12, "muted"));
            var tileContent = new Grid { ColumnSpacing = 14 }; tileContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); tileContent.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); tileContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            tileContent.Children.Add(symbol); Grid.SetColumn(text, 1); tileContent.Children.Add(text); var arrow = g.Icon("\uE76C", 12, "muted"); Grid.SetColumn(arrow, 2); tileContent.Children.Add(arrow); tile.Content = tileContent;
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch; tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            tile.Padding = new(16); tile.MinHeight = 84; tile.CornerRadius = new(18); tile.Background = g.Brush("surface", g.Solid ? 1 : 0); tile.BorderThickness = new(0);
            Grid.SetColumn(tile, i % 2); Grid.SetRow(tile, i / 2); features.Children.Add(tile);
        }
        return Stack(22, Heading("", "Home", "A little closer to your phone."), connection, g.Text("Your controls", 16, "text", true), features,
            Row(8, g.Icon("\uE72E", 14, "muted"), Live(() => session.Server.State.Microphone || session.Server.State.Camera ? $"{(session.Server.State.Microphone ? "Microphone active" : "")}{(session.Server.State.Microphone && session.Server.State.Camera ? " · " : "")}{(session.Server.State.Camera ? "Camera active" : "")}" : "Your microphone and camera are off.", 12, "muted")));
    }

    private StackPanel Microphone()
    {
        var waveform = new GlassAudioVisualizer(g) { Height = 72 }; audioView = waveform;
        waveform.Loaded += (_, _) => waveform.SetStreaming(session.Server.State.Microphone);
        var level = g.Text($"{preferences.Gain:0}%", 14, "accent", true);
        var gain = g.Slider("Input gain", preferences.Gain, v => { preferences.Gain = v; session.Gain = (float)v / 100; level.Text = $"{v:0}%"; preferences.SaveSoon(); });
        var mute = g.Button(preferences.Muted ? "Unmute" : "Mute", () => { preferences.Muted = !preferences.Muted; session.Muted = preferences.Muted; preferences.Save(); pageCache.Remove("Microphone"); RenderPage(false); }, true, preferences.Muted ? "\uE198" : "\uE720");
        var console = g.Surface(Stack(18, Row(12, g.Icon("\uE720", 20), g.Text("Input monitor", 16, "text", true), LiveStatus(() => session.Server.State.Microphone ? "Live" : "Off")), waveform, Row(16, mute, Live(() => preferences.Muted ? "Microphone muted" : session.Server.State.Microphone ? "Receiving phone audio" : "Start the microphone on your phone", 13, "muted")), gain, level), "Regular");
        var outputHeader = new Grid { ColumnSpacing = 16 }; outputHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); outputHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        outputHeader.Children.Add(Stack(8, Row(10, g.Icon("\uE720", 18), g.Text("Windows microphone", 17, "text", true)), Live(() => session.MicrophoneOutput.CaptureEndpoint, 13, "muted")));
        var outputActions = Row(10, LiveButton(() => session.MicrophoneOutput.Routing ? "Stop sharing" : "Share with apps", ToggleMicrophoneOutput, true), g.Button("Setup", MicrophoneSetupDetails)); Grid.SetColumn(outputActions, 1); outputHeader.Children.Add(outputActions);
        var output = g.Surface(Stack(14, outputHeader, Live(() => session.MicrophoneOutput.Error ?? (session.MicrophoneOutput.Routing ? "Select the recording device above in Meet or your audio app." : "A recording device is required for Meet and other apps."), 12, "muted")), "Subtle", 20);
        return Stack(20, Heading("", "Microphone", "Your voice, in focus."), output, console,
            g.Card(Stack(12, g.Switch("Monitor on speakers", session.Monitoring, v => { try { session.Monitor(v); } catch (Exception e) { _ = g.Dialog(root.XamlRoot, "Audio output unavailable", g.Text(e.Message)); } }), g.Text("Hear a local preview. This does not create a recording device.", 12, "muted"))),
            Row(24, g.Text("48 kHz · 16-bit · mono", 12, "muted"), Live(() => $"{session.Server.AudioBytes / 1024} KB received · {session.Device.LatencyMs?.ToString("0") ?? "—"} ms", 12, "muted")));
    }
    private async void ToggleMicrophoneOutput()
    {
        try { await Task.Run(() => { if (session.MicrophoneOutput.Routing) session.MicrophoneOutput.Stop(); else session.MicrophoneOutput.Start(); }); }
        catch (Exception error) { await g.Dialog(root.XamlRoot, "Microphone component unavailable", g.Text(error.Message)); }
        finally { pageCache.Remove("Microphone"); if (page == "Microphone") RenderPage(false); }
    }
    private async void MicrophoneSetupDetails()
    {
        var origin = new HyperlinkButton { Content = "VB-Audio · donationware · support the developer", NavigateUri = new Uri("https://vb-audio.com/Cable/") };
        var errorLabel = g.Text("", 12, "danger");
        var open = g.Button("Open bundled setup folder", () => { try { MicrophoneSetup.OpenPackage(); } catch (Exception error) { errorLabel.Text = error.Message; StartupDiagnostics.Write("Microphone setup package", error); } });
        await g.Dialog(root.XamlRoot, "Set up your Windows microphone", Stack(16,
            g.Text("The bundled VB-CABLE component creates a recording device that Meet and other apps can select.", 14),
            g.Text("Open the folder, run VBCABLE_Setup_x64.exe as administrator, then restart Windows. Its original installer and license are provided unchanged.", 14, "muted"),
            open, errorLabel, g.Text("After setup, choose CABLE Output (VB-Audio Virtual Cable) as the microphone in Meet. Synqora routes your phone’s audio to it automatically when capture starts.", 14), origin));
    }
    private StackPanel Metric(string label, string value) => Stack(8, g.Text(label, 10, "muted", true), g.Text(value, 21, "text", true));
    private StackPanel Webcam()
    {
        var view = new Grid { Height = compact ? 270 : 330, Background = g.Brush("background"), CornerRadius = new(24) };
        Glass.RoundClip(view, 20);
        var center = Stack(14, g.Icon("\uE714", 36), g.Text("Your phone, in full view.", 23, "text", true), g.Text("Start the camera on your phone.", 14, "muted")); center.HorizontalAlignment = HorizontalAlignment.Center; center.VerticalAlignment = VerticalAlignment.Center;
        view.Children.Add(center);
        cameraImage = new Image { Stretch = preferences.CameraFill ? Stretch.UniformToFill : Stretch.Uniform }; view.Children.Add(cameraImage);
        var image = cameraImage; image.Unloaded += (_, _) => image.Source = null;
        liveBindings.Add(() => center.Visibility = session.Server.State.Camera ? Visibility.Collapsed : Visibility.Visible);
        var label = LiveStatus(() => session.Server.State.Camera ? "Live camera" : "Camera off"); label.Margin = new(16); label.VerticalAlignment = VerticalAlignment.Top; view.Children.Add(label);
        var tools = g.Surface(Row(12, g.IconButton("\uE713", "Camera settings", CameraSettings), Live(() => session.Server.State.Camera ? $"{videoDimensions} · {decodedFps:0} fps" : "Waiting for your phone", 12, "muted")), "Elevated", 6);
        tools.Margin = new(16); tools.HorizontalAlignment = HorizontalAlignment.Center; tools.VerticalAlignment = VerticalAlignment.Bottom; tools.CornerRadius = new(30); view.Children.Add(tools);
        var publish = g.Button(cameraPublishing ? "Starting camera…" : cameraBridge?.Published == true ? "Stop sharing camera" : "Share with Windows apps", TogglePublishedCamera, true, "\uE714");
        publish.IsEnabled = !cameraPublishing;
        return Stack(20, Heading("", "Webcam", "A clear view from your phone."), g.Surface(view, "Regular", 6),
            g.Card(Stack(14, Row(16, publish, LiveStatus(() => cameraBridge?.Published == true ? "Available to apps" : "Preview only")),
                g.Text(cameraBridge?.Published == true ? "Select Synqora Camera in Meet or your camera app." : "Sharing starts automatically with your phone camera. Windows 11 and the latest Synqora installation are required.", 13, "muted"), Live(() => cameraBridgeError, 12, "danger"))));
    }
    private async void TogglePublishedCamera() => await PublishCamera(false);
    private async Task PublishCamera(bool automatic)
    {
        if (cameraPublishing) return;
        if (automatic && cameraBridge?.Published == true) return;
        if (cameraBridge == null) { if (!automatic) await g.Dialog(root.XamlRoot, "Camera unavailable", g.Text(cameraBridgeError)); return; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { cameraBridgeError = "Windows 11 is required to share the camera with other apps."; if (!automatic) await g.Dialog(root.XamlRoot, "Windows 11 required", g.Text(cameraBridgeError)); return; }
        if (!CameraBridge.Installed) { cameraBridgeError = "Install the latest Synqora MSI to enable Windows camera sharing."; if (!automatic) await g.Dialog(root.XamlRoot, "Install the camera component", g.Text(cameraBridgeError)); return; }
        cameraPublishing = true; pageCache.Remove("Webcam"); if (page == "Webcam") RenderPage(false);
        try { await Task.Run(() => { if (cameraBridge.Published) cameraBridge.Stop(); else cameraBridge.Start(); }); cameraBridgeError = ""; }
        catch (Exception error) { cameraBridgeError = error.Message; StartupDiagnostics.Write("Windows camera sharing", error); if (!automatic) await g.Dialog(root.XamlRoot, "Couldn’t share the camera", g.Text(cameraBridgeError)); }
        finally { cameraPublishing = false; pageCache.Remove("Webcam"); if (page == "Webcam") RenderPage(false); }
    }
    private async void CameraSettings() => await g.Dialog(root.XamlRoot, "Camera preferences", Stack(16,
        g.Dropdown("Framing in Windows apps", ["Fit · full image", "Fill · crop edges"], preferences.CameraFill ? "Fill · crop edges" : "Fit · full image", value => { preferences.CameraFill = value.StartsWith("Fill"); preferences.Save(); cameraBridge?.Framing(preferences.CameraFill); if (cameraImage != null) cameraImage.Stretch = preferences.CameraFill ? Stretch.UniformToFill : Stretch.Uniform; }),
        g.Text("Fit keeps the whole picture. Fill crops the edges to remove bars; it never stretches the image.", 13, "muted"),
        g.Text("Start or stop the camera and choose video quality on your phone.", 14), Live(() => session.Server.State.Camera ? $"Receiving {videoDimensions} over encrypted MJPEG." : "Your phone camera is off.", 13, "muted")));
    private StackPanel Touchpad()
    {
        return Stack(24, Heading("", "Touchpad", "A natural extension of your PC."), g.Surface(Stack(20,
            Row(16, g.Surface(g.Icon("\uE7C9", 28), "Elevated", 16), Stack(6, g.Text("Control from your phone", 22, "text", true), Live(() => session.CanStream ? session.Device.Name : "Connect a phone to begin", 14, "muted"))),
            ControlSwitch(), LiveStatus(() => session.CanStream && session.Server.State.ControlsAllowed ? "Ready for gestures" : "Phone controls paused")), "Regular"),
            g.Card(Stack(18, g.Text("On your phone", 17, "text", true), Guide("One finger", "Move the pointer"), Guide("Two fingers", "Scroll the page"), Guide("Tap", "Click the focused item"))), g.Button("Connection details", ConnectionDetails, glyph: "\uE8F2"));
    }
    private StackPanel Keyboard()
    {
        return Stack(24, Heading("", "Keyboard", "Your words, on the bigger screen."), g.Surface(Stack(20, Row(16, g.Surface(g.Icon("\uE765", 28), "Elevated", 16), Stack(6, g.Text("Live typing", 22, "text", true), Live(() => session.CanStream ? session.Device.Name : "Connect a phone to begin", 14, "muted"))), ControlSwitch(), LiveStatus(() => session.CanStream && session.Server.State.ControlsAllowed ? "Ready to receive input" : "Phone controls paused")), "Regular"),
            g.Card(Stack(18, g.Text("Ready when you are", 17, "text", true), Guide("1", "Focus a text field in any PC app"), Guide("2", "Open Keyboard on your phone"), Guide("3", "Type, edit and use shortcuts live"))), g.Button("Connection details", ConnectionDetails));
    }

    private StackPanel Guide(string label, string description) => Row(20, g.Text(label, 14, "text", true), g.Text(description, 14, "muted"));

    private StackPanel Settings()
    {
        return Stack(24, Heading("Make it yours", "Settings", "Appearance, comfort, and connection."),
            g.Card(Stack(20, Row(12, g.Icon("\uE790"), g.Text("Appearance", 20, "text", true)), g.Dropdown("Color theme", ["System", "Light", "Dark"], preferences.Theme, v => { preferences.Theme = v; preferences.Save(); Build(); }), g.Dropdown("Visual effects quality", ["Balanced", "Economy", "High"], preferences.Effects, v => { preferences.Effects = v; preferences.Save(); Build(); }), g.Text("Economy uses solid surfaces. High keeps richer materials where the system permits.", 13, "muted"))),
            g.Card(Stack(20, Row(12, g.Icon("\uE776"), g.Text("Comfort & accessibility", 20, "text", true)), g.Switch("Reduce motion", preferences.ReducedMotion, v => { preferences.ReducedMotion = v; preferences.Save(); }), g.Switch("Reduce transparency", preferences.ReducedTransparency, v => { preferences.ReducedTransparency = v; preferences.Save(); Build(); }), g.Text("Windows animation, transparency, and high contrast preferences take priority automatically.", 13, "muted"))),
            g.Card(Stack(16, g.Text("Connection & privacy", 20, "text", true), Live(() => session.CanStream ? $"Encrypted local connection to {session.Device.Name}." : "No phone connected. Your microphone and camera are off.", 14, "muted"), ControlSwitch(), g.Button("Device details", ConnectionDetails))),
            g.Text("Synqora 0.3.0", 11, "muted"));
    }
    private async Task CapturePreviews()
    {
        var index = Array.IndexOf(args, "--capture");
        var directory = index >= 0 && args.Length > index + 1 ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(directory);
        var originalTheme = preferences.Theme;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            preferences.Theme = theme; Build(); await Task.Delay(300);
            foreach (var destination in pages)
            {
                Navigate(destination); await Task.Delay(450);
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
                var pixels = await bitmap.GetPixelsAsync();
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(directory));
                var target = await folder.CreateFileAsync($"windows-{theme.ToLowerInvariant()}-{destination.ToLowerInvariant()}.png", CreationCollisionOption.ReplaceExisting);
                using var stream = await target.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
            }
        }
        if (!args.Contains("--no-measure"))
        {
        var samples = new List<double>(); var watch = Stopwatch.StartNew(); double previous = -1;
        EventHandler<object> measure = (_, _) => { var now = watch.Elapsed.TotalMilliseconds; if (previous >= 0) samples.Add(now - previous); previous = now; };
        CompositionTarget.Rendering += measure;
        for (int i = 0; i < 20; i++) { Navigate(pages[i % pages.Length]); await Task.Delay(100); }
        CompositionTarget.Rendering -= measure;
        samples.Sort();
        File.WriteAllText(Path.Combine(directory, "windows-frame-times.json"), JsonSerializer.Serialize(new { scenario = "20 immediate page changes; idle media; native compositor", count = samples.Count, medianMs = samples.Count == 0 ? 0 : samples[samples.Count / 2], p95Ms = samples.Count == 0 ? 0 : samples[(int)(samples.Count * .95)], over33ms = samples.Count(v => v > 33.34), streamingValidated = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        preferences.Theme = originalTheme; Build();
        File.WriteAllText(Path.Combine(directory, "capture.complete"), "Native WinUI captures complete.");
        if (args.Contains("--exit")) Close();
    }
    private async Task SaveScreen(string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync();
        // File stream avoids any desktop capture or input injection.
        using var stream = File.Open(path, FileMode.Create).AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }
    private async Task ReceiverTest()
    {
        var at = Array.IndexOf(args, "--receiver-test");
        var folder = Path.GetFullPath(args[at + 1]); Directory.CreateDirectory(folder);
        try {
            session.Start(0, true);
            File.WriteAllText(Path.Combine(folder, "backend-invite.txt"), session.Server.Invitation("127.0.0.1"));
            var watch = Stopwatch.StartNew();
            while (decodedFrames < 8 || session.Server.AudioBytes == 0) {
                if (watch.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Native receiver did not receive both media streams.");
                await Task.Delay(100);
            }
            if (cameraBridge == null) throw new InvalidOperationException("Native camera bridge unavailable: " + cameraBridgeError);
            var native = await Task.Run(cameraBridge.Inspect);
            if (native.Width != 1280 || native.Height != 720 || native.MaximumLuma <= native.MinimumLuma) throw new InvalidDataException("Native camera source did not receive the actual emulated phone picture.");
            Navigate("Webcam"); await Task.Delay(1000);
            await SaveScreen(Path.Combine(folder, "windows-live-webcam.png"));
            Navigate("Microphone"); await Task.Delay(1200);
            await SaveScreen(Path.Combine(folder, "windows-live-microphone.png"));
            var frameIntervals = new List<double>(); var clock = Stopwatch.StartNew(); double previous = -1;
            EventHandler<object> sample = (_, _) => { var now = clock.Elapsed.TotalMilliseconds; if (previous >= 0) frameIntervals.Add(now - previous); previous = now; };
            CompositionTarget.Rendering += sample;
            try {
                for (int i = 0; i < 24; i++) { Navigate(pages[i % pages.Length]); await Task.Delay(180); }
            } finally { CompositionTarget.Rendering -= sample; }
            frameIntervals.Sort();
            File.WriteAllText(Path.Combine(folder, "windows-receiver-metrics.json"), JsonSerializer.Serialize(new { scenario = "Android CameraX + AudioRecord → pinned TLS → WinUI + original Media Foundation camera source; offscreen captures; no OS input injection", session.Server.AudioBytes, session.Server.VideoFrames, decodedFrames, videoDimensions, nativeSource = new { native.Width, native.Height, native.Bytes, native.Checksum, native.MinimumLuma, native.MaximumLuma }, uiFrameCallbacks = new { scenario = "24 immediate page changes during simultaneous streaming; UI rendering callbacks, not GPU present times", count = frameIntervals.Count, p50Ms = frameIntervals.Count == 0 ? 0 : frameIntervals[frameIntervals.Count / 2], p95Ms = frameIntervals.Count == 0 ? 0 : frameIntervals[(int)(frameIntervals.Count * .95)], over33ms = frameIntervals.Count(v => v > 33.34) }, remoteControlsAllowed = session.Server.State.ControlsAllowed, frameError = mediaError, cameraBridgeError }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception error) { File.WriteAllText(Path.Combine(folder, "windows-receiver-error.txt"), error.ToString()); }
        finally { Close(); }
    }
}
