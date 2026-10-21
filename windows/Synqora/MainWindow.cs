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
using Synqora.Core;
using System.Threading.Channels;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.ViewManagement;
using static Synqora.Design.Glass;

namespace Synqora;

public sealed partial class MainWindow : Window
{
    private readonly Preferences preferences = Preferences.Load();
    private readonly WindowsSession session = new();
    private readonly FileExchange files;
    private readonly CompanionInbox inbox;
    private readonly Gallery gallery;
    private readonly PhoneCalls calls;
    private readonly BluetoothCalls bluetooth = new();
    private TrayIcon? tray;
    private bool quitting;
    private ContentDialog? pairingDialog;
    private StackPanel? fileList, notificationList, callList;
    private readonly Dictionary<string, (string Version, UIElement Card)> notificationCards = [];
    private string companionError = "";
    private readonly List<Action> liveBindings = [];
    private readonly Dictionary<string, List<Action>> pageBindings = [];
    private List<Action>? buildingBindings;
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
    private Grid pageHost = null!;
    private ColumnDefinition sidebarColumn = null!;
    private TextBlock sidebarLabel = null!;
    private Button connectionButton = null!;
    private readonly Dictionary<string, TextBlock> navLabels = [];
    private ScrollViewer scroll = null!;
    private readonly string[] pages = ["Overview", "Inbox", "Files", "Photos", "Calls", "Microphone", "Webcam", "Touchpad", "Keyboard", "Settings"];
    private readonly string[] glyphs = ["\uE80F", "\uE7E7", "\uE8B7", "\uEB9F", "\uE717", "\uE720", "\uE714", "\uE7C9", "\uE765", "\uE713"];
    private string page = "Overview";
    private readonly Dictionary<string, double> scrollPositions = [];
    private readonly Dictionary<string, StackPanel> pageCache = [];
    private readonly Dictionary<string, Button> navButtons = [];
    private string mediaError = "";
    private readonly UISettings system = new();
    private readonly AccessibilitySettings accessibility = new();
    private readonly string[] args;
    private bool capturing;
    private bool lastHighContrast;
    private volatile bool previewVisible = true;

    public MainWindow(string[] arguments)
    {
        args = arguments; g = new Glass(preferences);
        preferences.Theme = "Dark";
        files = new FileExchange(session.Server); inbox = new CompanionInbox(session.Server);
        gallery = new Gallery(session.Server, files);
        calls = new PhoneCalls(session.Server);
        calls.Changed += refresh => DispatcherQueue.TryEnqueue(() => { RefreshCallHistory(); if (refresh && page == "Calls") calls.Refresh(); });
        bluetooth.Changed += () => DispatcherQueue.TryEnqueue(RefreshBluetoothCalls);
        gallery.Changed += id => DispatcherQueue.TryEnqueue(() => GalleryChanged(id));
        files.Changed += () => DispatcherQueue.TryEnqueue(RefreshFiles);
        inbox.Changed += () => DispatcherQueue.TryEnqueue(RefreshNotifications);
        try { cameraBridge = new CameraBridge(); cameraBridge.Framing(preferences.CameraFill); }
        catch (Exception error) { cameraBridgeError = error.Message; }
        session.Gain = (float)preferences.Gain / 100;
        session.Muted = preferences.Muted;
        Title = "Synqora · 0.5 Preview";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "synqora.ico"));
        int ArgInt(string key, int fallback) { var at = Array.IndexOf(args, key); return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback; }
        AppWindow.Resize(new SizeInt32(Math.Max(700, ArgInt("--width", 1080)), Math.Max(600, ArgInt("--height", 820))));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 700;
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        Build();
        if (!args.Contains("--capture") && !args.Contains("--receiver-test")) {
            try {
                tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), Path.Combine(AppContext.BaseDirectory, "Assets", "synqora.ico"), ShowWindow, ConnectionDetails, () => _ = session.DisconnectAsync(), () => { quitting = true; Close(); }, () => preferences.MinimizeToTray);
                tray.showHidden = () => DispatcherQueue.TryEnqueue(() => AppWindow.Hide());
                AppWindow.Closing += (_, e) => { if (!quitting && preferences.CloseToTray) { e.Cancel = true; AppWindow.Hide(); } };
            } catch (Exception error) { StartupDiagnostics.Write("Tray icon", error); }
        }
        session.Changed += _ => DispatcherQueue.TryEnqueue(() => {
            if (session.CanStream) pairingDialog?.Hide();
            tray?.Status(session.CanStream ? $"Connected to {session.Device.Name}" : "No phone connected");
            foreach (var update in liveBindings.ToArray()) update();
            audioView?.SetStreaming(previewVisible && session.Server.State.Microphone && page == "Microphone");
            if (session.CanStream && previewVisible) liveTimer.Start(); else { liveTimer.Stop(); if (!session.CanStream) { videoDimensions = "—"; if (cameraImage != null) cameraImage.Source = null; } }
        });
        liveTimer.Tick += (_, _) => { foreach (var update in liveBindings.ToArray()) update(); };
        AppWindow.Changed += (_, _) => {
            previewVisible = AppWindow.IsVisible && (AppWindow.Presenter is not OverlappedPresenter p || p.State != OverlappedPresenterState.Minimized);
            audioView?.SetStreaming(previewVisible && page == "Microphone" && session.Server.State.Microphone);
            if (previewVisible && session.CanStream) liveTimer.Start(); else liveTimer.Stop();
        };
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
        Closed += (_, _) => { bluetooth.Dispose(); tray?.Dispose(); liveTimer.Stop(); preferences.Save(); mediaCancellation.Cancel(); jpegFrames.Writer.TryComplete(); session.Dispose(); _ = Task.Run(() => cameraBridge?.Dispose()); };
        system.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (preferences.Theme == "System") Build(); });
        system.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        system.AnimationsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        Windows.System.Power.PowerManager.EnergySaverStatusChanged += (_, _) => DispatcherQueue.TryEnqueue(Build);
        try { accessibility.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(Build); }
        catch (System.Runtime.InteropServices.COMException) { /* Some unpackaged environments do not expose this event. Refresh on activation below. */ }
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated && lastHighContrast != g.HighContrast) Build(); };
    }
    private void ShowWindow() { AppWindow.Show(); if (AppWindow.Presenter is OverlappedPresenter p) p.Restore(); Activate(); }
    private void Build()
    {
        liveBindings.Clear(); pageBindings.Clear(); audioView = null; cameraImage = null; fileList = null; notificationList = null; callList = null; notificationCards.Clear(); photos = null; photoStatus = null; photoImages.Clear(); loadedPhotos.Clear(); photoGeneration++; recentCalls = null; dialNumber = null; dialButton = null; callAccess = null; dialResult = null; bluetoothCallList = null; bluetoothStatus = null;
        if (scroll is not null) scrollPositions[page] = scroll.VerticalOffset;
        pageCache.Clear();
        lastHighContrast = g.HighContrast;
        g.Dark = true;
        SystemBackdrop = !g.Solid && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() ? new ClearBackdrop(g) : !g.Solid && Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() ? new MicaBackdrop() : null;
        root = new Grid { Background = g.Brush("background", g.Solid ? 1 : .08), RequestedTheme = g.Dark ? ElementTheme.Dark : ElementTheme.Light };

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
        root.Resources["ContentDialogBackground"] = g.Brush("solid");
        root.Resources["ContentDialogSmokeFill"] = g.Brush("background", .35);
        root.Resources["ContentDialogTopOverlay"] = g.Brush("surface");
        root.Resources["ContentDialogBorderBrush"] = g.Brush("text", .13);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = Row(10, Brand.Mark(g, 28), g.Text("Synqora", 14, "text", true), g.Text("Preview 0.5", 11, "muted"));
        title.Margin = new(24, 0, 150, 0); title.Background = g.Brush("background", .01);
        root.Children.Add(title); SetTitleBar(title);
        AppWindow.TitleBar.ButtonForegroundColor = g.Color("text");
        shell = new Grid { Margin = new(12, 0, 12, 12), ColumnSpacing = 12 };
        sidebarColumn = new ColumnDefinition { Width = new(216) }; shell.ColumnDefinitions.Add(sidebarColumn); shell.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetRow(shell, 1); root.Children.Add(shell);
        var sidebar = new Grid(); sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto }); sidebar.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        sidebarLabel = g.Text("Workspace", 12, "muted"); sidebarLabel.Margin = new(14, 14, 0, 16); sidebar.Children.Add(sidebarLabel);
        nav = Stack(5); nav.HorizontalAlignment = HorizontalAlignment.Stretch;
        var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Disabled }; Grid.SetRow(navScroll, 1); sidebar.Children.Add(navScroll); BuildNav();
        connectionButton = g.Button("Connect phone", ConnectionDetails, glyph: "\uE8F2"); connectionButton.HorizontalAlignment = HorizontalAlignment.Stretch; connectionButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        var footer = Stack(12, connectionButton); footer.Margin = new(0, 20, 0, 4); Grid.SetRow(footer, 2); sidebar.Children.Add(footer);
        var navigation = g.Surface(sidebar, "Regular", 8); navigation.CornerRadius = new(20); shell.Children.Add(navigation);
        scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        pageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(32, 28, 32, 36) }; scroll.Content = pageHost;
        var workspace = new Border { Background = g.Brush("background", g.Solid ? 1 : .2), CornerRadius = new(20), Child = scroll, BorderBrush = g.Brush("text", .04), BorderThickness = new(1) }; Grid.SetColumn(workspace, 1); shell.Children.Add(workspace);
        scroll.SizeChanged += (_, _) => SizeWorkspace();
        root.SizeChanged += (_, e) => { SizeSidebar(e.NewSize.Width); SizeWorkspace(); };
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
        navLabels.Clear();
        for (int i = 0; i < pages.Length; i++)
        {
            var dest = pages[i]; var selected = dest == page;
            var b = g.Button(dest == "Overview" ? "Overview" : dest, () => Navigate(dest));
            var label = g.Text(dest, 14); navLabels[dest] = label; b.Content = Row(12, g.Icon(glyphs[i], 19, "text"), label);
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.MinWidth = 0; b.Padding = new(14, 12, 14, 12);
            b.Background = g.Brush("text", selected ? .09 : 0); b.BorderThickness = new(0); b.CornerRadius = new(12);
            b.Resources["ButtonBackgroundPointerOver"] = g.Brush("text", .06); b.Resources["ButtonBackgroundPressed"] = g.Brush("text", .1); ToolTipService.SetToolTip(b, dest);
            AutomationProperties.SetAutomationId(b, "nav-" + dest);
            AutomationProperties.SetItemStatus(b, selected ? "Current page" : "");
            b.AccessKey = (i + 1).ToString();
            navButtons[dest] = b;
            nav.Children.Add(b);
        }
    }
    private void SizeSidebar(double width)
    {
        var next = width < 940; sidebarColumn.Width = new(next ? 72 : 216);
        sidebarLabel.Visibility = next ? Visibility.Collapsed : Visibility.Visible;
        foreach (var label in navLabels.Values) label.Visibility = next ? Visibility.Collapsed : Visibility.Visible;
        connectionButton.Content = next ? g.Icon("\uE8F2", 19) : Row(12, g.Icon("\uE8F2", 19), g.Text("Connect phone", 14));
        connectionButton.Padding = new(next ? 14 : 18, 11, next ? 14 : 18, 11);
    }
    private void SizeWorkspace()
    {
        if (scroll == null || pageHost == null) return;
        var inset = scroll.ActualWidth < 620 ? 22 : 36;
        pageHost.Margin = new(inset, 28, inset, 36);
        var available = Math.Max(0, scroll.ActualWidth - inset * 2 - 2);
        pageHost.Width = available;
        if (pageHost.Children.FirstOrDefault() is FrameworkElement content) content.Width = Math.Min(900, available);
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
            button.Background = g.Brush("text", selected ? .09 : 0);
        }
        RenderPage(false);
        if (dest == "Photos" && galleryDirty) { galleryDirty = false; gallery.Page(); }
        if (dest == "Calls") calls.Refresh();
    }
    private void RenderPage(bool animate = false)
    {
        if (!pageCache.TryGetValue(page, out var content))
        {
            if (pageBindings.Remove(page, out var previous)) foreach (var binding in previous) liveBindings.Remove(binding);
            buildingBindings = []; pageBindings[page] = buildingBindings;
            content = page switch { "Overview" => Dashboard(), "Inbox" => InboxPage(false), "Files" => FilesPage(), "Photos" => PhotosPage(), "Calls" => CallsPage(), "Microphone" => Microphone(), "Webcam" => Webcam(), "Touchpad" => Touchpad(), "Keyboard" => Keyboard(), _ => Settings() };
            buildingBindings = null;
            pageCache[page] = content;
        }
        content.MaxWidth = double.PositiveInfinity; content.HorizontalAlignment = HorizontalAlignment.Center;
        pageHost.Children.Clear(); pageHost.Children.Add(content); SizeWorkspace();
        audioView?.SetStreaming(previewVisible && page == "Microphone" && session.Server.State.Microphone);
        var destination = page;
        DispatcherQueue.TryEnqueue(() => { if (page == destination) scroll.ChangeView(null, scrollPositions.GetValueOrDefault(destination), null, true); });
        if (animate) g.Enter(content);
    }
    private StackPanel Heading(string eyebrow, string title, string subtitle) => Stack(8, g.Display(title, 28), g.Text(subtitle, 14, "muted"));
    private async void ConnectionDetails()
    {
        if (pairingDialog != null) return;
        if (session.CanStream) { await g.Dialog(root.XamlRoot, "Your phone", Stack(16, g.Text(session.Device.Name, 22, "text", true), g.Text("Connected securely over your local network.", 14, "muted"), ControlSwitch(), g.Button("Disconnect", () => _ = session.DisconnectAsync()))); return; }
        try { session.Start(); }
        catch (Exception error) { await g.Dialog(root.XamlRoot, "Could not start connection", g.Text(error.Message)); return; }
        var invitation = session.Server.Invitation();
        using var qrData = QRCoder.QRCodeGenerator.GenerateQrCode(invitation, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCoder.PngByteQRCode(qrData);
        using var qrStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await qrStream.WriteAsync(qr.GetGraphic(6).AsBuffer()); qrStream.Seek(0);
        var qrImage = new BitmapImage(); await qrImage.SetSourceAsync(qrStream);
        var qrPanel = new Border { Background = new SolidColorBrush(Colors.White), Padding = new(12), CornerRadius = new(20), HorizontalAlignment = HorizontalAlignment.Center, Child = new Image { Source = qrImage, Width = 216, Height = 216 } };
        AutomationProperties.SetName(qrPanel, "Pairing QR code. The same invitation is available in the pairing link field.");
        var copy = g.Button("Copy pairing link", () => { var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(invitation); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data); }, true, "\uE8C8");
        copy.HorizontalAlignment = HorizontalAlignment.Center;
        var content = Stack(20, g.Text("Open Synqora on your phone, tap Connect a computer, then scan this code.", 14, "muted"), qrPanel, copy, g.Text("Same Wi-Fi · encrypted connection · code expires in 10 minutes", 12, "muted"));
        content.MaxWidth = 360;
        pairingDialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Connect your phone", Content = content, CloseButtonText = "Cancel", Background = g.Brush("solid"), Foreground = g.Brush("text"), CornerRadius = new(28) };
        try { if (!session.CanStream) await pairingDialog.ShowAsync(); }
        finally { pairingDialog = null; }
    }
    private void Bind(Action binding) { liveBindings.Add(binding); buildingBindings?.Add(binding); }
    private TextBlock Live(Func<string> value, double size = 14, string token = "text") { var label = g.Text(value(), size, token); Bind(() => label.Text = value()); return label; }
    private Button LiveButton(Func<string> value, Action action, bool primary = false) {
        var button = g.Button(value(), action, primary); var label = g.Text(value(), 14, primary ? "onAccent" : "text", true); button.Content = label;
        Bind(() => { var next = value(); if (label.Text != next) { label.Text = next; AutomationProperties.SetName(button, next); } }); return button;
    }
    private Border LiveStatus(Func<string> value) { var panel = g.Surface(Live(value, 12, "muted"), "Subtle", 10); panel.CornerRadius = new(18); panel.HorizontalAlignment = HorizontalAlignment.Left; return panel; }
    private ToggleSwitch ControlSwitch() { var toggle = g.Switch("Allow phone keyboard & pointer", session.Server.State.ControlsAllowed, session.Server.AllowControls); Bind(() => { if (toggle.IsOn != session.Server.State.ControlsAllowed) toggle.IsOn = session.Server.State.ControlsAllowed; }); return toggle; }
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
                if (!previewVisible || Interlocked.CompareExchange(ref presentationPending, 1, 0) != 0) { bitmap.Dispose(); continue; }
                if (!DispatcherQueue.TryEnqueue(async () => {
                    try {
                        if (!session.Server.State.Camera || frame.Generation != (cameraBridge?.Generation ?? 0)) return;
                        videoDimensions = dimensions;
                        var target = cameraImage;
                        if (previewVisible && target != null && page == "Webcam" && target.IsLoaded) {
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
        var details = Stack(6, g.Text("This computer", 12, "muted"), g.Display(args.Contains("--capture") ? "Windows PC" : Environment.MachineName, 28), Live(() => session.CanStream ? $"Connected to {session.Device.Name}" : "No phone connected", 14, "muted"));
        var connect = LiveButton(() => session.CanStream ? "Device details" : "Connect phone", ConnectionDetails, true);
        var connection = g.Between(g.Leading(g.Icon("\uE7F4", 30, "text"), details), connect);
        var features = Stack(0);
        string[] names = ["Microphone", "Webcam", "Touchpad", "Keyboard"];
        string[] descriptions = ["Use your phone's microphone in calls and recordings", "Bring your phone camera to your PC", "Move, click and scroll from your phone", "Type and use shortcuts in your PC apps"];
        for (int i = 0; i < names.Length; i++) {
            var name = names[i]; var tile = g.Button(name, () => Navigate(name));
            var symbol = g.Icon(glyphs[Array.IndexOf(pages, name)], 22, "text");
            var text = Stack(4, g.Text(name, 17, "text", true), g.Text(descriptions[i], 12, "muted"));
            var tileContent = new Grid { ColumnSpacing = 20 }; tileContent.ColumnDefinitions.Add(new() { Width = new(32) }); tileContent.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); tileContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            tileContent.Children.Add(symbol); Grid.SetColumn(text, 1); tileContent.Children.Add(text); var arrow = g.Icon("\uE76C", 12, "muted"); Grid.SetColumn(arrow, 2); tileContent.Children.Add(arrow); tile.Content = tileContent;
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch; tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            tile.Padding = new(16, 18, 16, 18); tile.MinHeight = 84; tile.CornerRadius = new(12); tile.Background = g.Brush("surface", 0); tile.BorderThickness = new(0);
            features.Children.Add(tile); if (i < 3) features.Children.Add(g.Rule());
        }
        return Stack(28, Heading("", "Your workspace", "One connection for your phone and computer."), g.Section(connection, 16), g.Rule(), features,
            Row(8, g.Icon("\uE72E", 13, "muted"), Live(() => session.CanStream ? $"Encrypted local connection · {session.Device.LatencyMs?.ToString("0") ?? "—"} ms" : "Pair a phone to get started. Your camera and microphone are off.", 12, "muted")));
    }

    private StackPanel Microphone()
    {
        var waveform = new GlassAudioVisualizer(g) { Height = 110 }; audioView = waveform;
        waveform.Loaded += (_, _) => waveform.SetStreaming(session.Server.State.Microphone);
        var level = g.Text($"{preferences.Gain:0}%", 14, "accent", true);
        var gain = g.Slider("Input gain", preferences.Gain, v => { preferences.Gain = v; session.Gain = (float)v / 100; level.Text = $"{v:0}%"; preferences.SaveSoon(); });
        var mute = g.Button(preferences.Muted ? "Unmute" : "Mute", () => { preferences.Muted = !preferences.Muted; session.Muted = preferences.Muted; preferences.Save(); pageCache.Remove("Microphone"); RenderPage(false); }, true, preferences.Muted ? "\uE198" : "\uE720");
        var console = g.Section(Stack(24, g.Between(g.Text("Input level", 15, "text", true), LiveStatus(() => session.Server.State.Microphone ? "Live" : "Off")), waveform,
            g.Between(Live(() => preferences.Muted ? "Microphone muted" : session.Server.State.Microphone ? "Receiving phone audio" : "Start the microphone on your phone", 14, "muted"), mute), g.Rule(), gain, level));
        var outputHeader = new Grid { ColumnSpacing = 16 }; outputHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); outputHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        outputHeader.Children.Add(Stack(8, Row(10, g.Icon("\uE720", 18), g.Text("Windows microphone", 17, "text", true)), Live(() => session.MicrophoneOutput.CaptureEndpoint, 13, "muted")));
        var outputActions = Row(10, LiveButton(() => session.MicrophoneOutput.Routing ? "Stop sharing" : "Share with apps", ToggleMicrophoneOutput, true), g.Button("Setup", MicrophoneSetupDetails)); Grid.SetColumn(outputActions, 1); outputHeader.Children.Add(outputActions);
        var output = g.Section(Stack(14, outputHeader, Live(() => session.MicrophoneOutput.Error ?? (session.MicrophoneOutput.Routing ? "Select the recording device above in Meet or your audio app." : "Set up a recording device to use phone audio in Meet and other apps."), 12, "muted")));
        return Stack(26, Heading("", "Microphone", "Phone audio, ready for your PC apps."), console, g.Rule(), output, g.Rule(),
            g.Section(Stack(10, g.Switch("Monitor on speakers", session.Monitoring, v => { try { session.Monitor(v); } catch (Exception e) { _ = g.Dialog(root.XamlRoot, "Audio output unavailable", g.Text(e.Message)); } }), g.Text("Hear a local preview. This does not create a recording device.", 12, "muted"))),
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
        var view = new Grid { Height = 350, Background = g.Brush("background", .65), CornerRadius = new(16) };
        view.SizeChanged += (_, e) => { var height = Math.Clamp(e.NewSize.Width * .5625, 250, 480); if (Math.Abs(view.Height - height) > 1) view.Height = height; };
        Glass.RoundClip(view, 20);
        var cameraGlyph = g.Icon("\uE714", 36); cameraGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        var emptyTitle = g.Text("Camera is off", 20, "text", true); emptyTitle.TextAlignment = TextAlignment.Center;
        var emptyHint = g.Text("Start the camera on your phone.", 14, "muted"); emptyHint.TextAlignment = TextAlignment.Center;
        var center = Stack(12, cameraGlyph, emptyTitle, emptyHint); center.HorizontalAlignment = HorizontalAlignment.Center; center.VerticalAlignment = VerticalAlignment.Center; center.Margin = new(24, 64, 24, 64);
        view.Children.Add(center);
        cameraImage = new Image { Stretch = preferences.CameraFill ? Stretch.UniformToFill : Stretch.Uniform }; view.Children.Add(cameraImage);
        var image = cameraImage; image.Unloaded += (_, _) => image.Source = null;
        Bind(() => center.Visibility = session.Server.State.Camera ? Visibility.Collapsed : Visibility.Visible);
        var label = LiveStatus(() => session.Server.State.Camera ? "Live camera" : "Camera off"); label.Margin = new(16); label.VerticalAlignment = VerticalAlignment.Top; view.Children.Add(label);
        var settings = g.IconButton("\uE713", "Camera settings", CameraSettings); settings.Margin = new(12); settings.HorizontalAlignment = HorizontalAlignment.Right; settings.VerticalAlignment = VerticalAlignment.Top; view.Children.Add(settings);
        var metrics = Live(() => session.Server.State.Camera ? $"{videoDimensions} · {decodedFps:0} fps" : "", 12, "muted"); metrics.Margin = new(20); metrics.VerticalAlignment = VerticalAlignment.Bottom; view.Children.Add(metrics);
        var publish = g.Button(cameraPublishing ? "Starting camera…" : cameraBridge?.Published == true ? "Stop sharing camera" : "Share with Windows apps", TogglePublishedCamera, true, "\uE714");
        publish.IsEnabled = !cameraPublishing;
        return Stack(24, Heading("", "Webcam", "A clear view from your phone."), view,
            g.Section(Stack(14, g.Between(Stack(6, g.Text("Windows camera", 16, "text", true), Live(() => cameraBridge?.Published == true ? "Available to apps" : "Preview only", 13, "muted")), publish),
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
        return Stack(28, Heading("", "Touchpad", "Control this PC from your phone."),
            g.Leading(g.Icon("\uE7C9", 36), Stack(8, g.Text("Ready for your gestures", 20, "text", true), Live(() => session.CanStream ? session.Device.Name : "Connect a phone to begin", 14, "muted"))),
            ControlSwitch(), g.Rule(), Guide("One finger", "Move the pointer"), Guide("Two fingers", "Scroll the page"), Guide("Tap", "Click the focused item"), g.Rule(),
            LiveStatus(() => session.CanStream && session.Server.State.ControlsAllowed ? "Ready for gestures" : "Phone controls paused"), g.Button("Connection details", ConnectionDetails, glyph: "\uE8F2"));
    }
    private StackPanel Keyboard()
    {
        return Stack(28, Heading("", "Keyboard", "Live typing from your phone."),
            g.Leading(g.Icon("\uE765", 36), Stack(8, g.Text("Every keystroke, directly", 20, "text", true), Live(() => session.CanStream ? session.Device.Name : "Connect a phone to begin", 14, "muted"))),
            ControlSwitch(), g.Rule(), Guide("01", "Focus a text field in any PC app"), Guide("02", "Open Keyboard on your phone"), Guide("03", "Type, edit and use shortcuts live"), g.Rule(),
            LiveStatus(() => session.CanStream && session.Server.State.ControlsAllowed ? "Ready to receive input" : "Phone controls paused"), g.Button("Connection details", ConnectionDetails));
    }

    private Grid Guide(string label, string description) { var lead = g.Text(label, 14, "text", true); lead.Width = 96; return g.Leading(lead, g.Text(description, 14, "muted")); }

    private StackPanel FilesPage() {
        fileList = Stack(12); RefreshFiles();
        return Stack(24, Heading("", "Files", "A direct route between your devices."),
            g.Section(Stack(16, g.Text("Send to your phone", 20, "text", true), g.Text("Choose a file of any size, within the available storage on your devices. Your phone asks where to save it. Transfers use a separate encrypted connection and are verified before saving.", 14, "muted"), g.Button("Choose a file", ChooseFile, true, "\uE8B7"), Live(() => companionError, 13, "danger"))),
            g.Text("Transfers", 17, "text", true), fileList);
    }
    private async void ChooseFile() {
        if (!session.CanStream) { companionError = "Connect your phone first."; return; }
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        try {
            var chosen = await picker.PickSingleFileAsync(); if (chosen == null) return;
            companionError = "Preparing file…"; await Task.Run(() => files.Offer(chosen.Path)); companionError = "";
        } catch (Exception error) { companionError = error.Message; }
        foreach (var update in liveBindings.ToArray()) update();
    }
    private void RefreshFiles() {
        if (fileList == null) return;
        fileList.Children.Clear();
        foreach (var file in files.Transfers.Take(30)) {
            var info = Stack(12, g.Text(file.Name, 16, "text", true), g.Text($"{Math.Max(1, file.Size / 1024):N0} KB · {(file.Incoming ? "From phone" : "To phone")} · {file.Status}", 13, "muted"));
            if (file.Status == "Transferring") info.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, file.Size), Value = file.Done, Foreground = g.Brush("accent") });
            if (file.Incoming && file.Status == "Waiting") info.Children.Add(Row(10, g.Button("Save to Downloads", () => {
                try { files.Accept(file.Id, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Synqora")); }
                catch (Exception error) { companionError = error.Message; }
            }, true), g.Button("Decline", () => files.Decline(file.Id))));
            else if (file.Status is "Waiting" or "Ready" or "Transferring") info.Children.Add(g.Button("Cancel", () => files.Decline(file.Id)));
            if (file.Status == "Complete" && file.Path != null) info.Children.Add(g.Button("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Path}\"") { UseShellExecute = true })));
            fileList.Children.Add(g.Surface(info, "Subtle", 18));
        }
        if (fileList.Children.Count == 0) fileList.Children.Add(g.Text("Files you send or receive appear here. Incoming files always ask for your approval.", 14, "muted"));
    }
    private StackPanel InboxPage(bool calls) {
        var list = Stack(12); if (calls) callList = list; else notificationList = list;
        var content = Stack(24, Heading("", calls ? "Calls" : "Inbox", calls ? "Stay in the conversation." : "Your phone, with fewer interruptions."),
            g.Section(Stack(12, g.Text(calls ? "Call controls from notifications" : "Enable notification sync on your phone", 19, "text", true),
                g.Text(calls ? "Incoming call notifications and available answer or decline actions appear here. The phone handles the call; Synqora does not route cellular or WhatsApp call audio to the PC." : "In Synqora on your phone, open Inbox, turn on notification sync and grant notification access. Reply works with WhatsApp, SMS, RCS and other notifications that expose a reply action.", 14, "muted"))),
            Live(() => inbox.Result, 14, "muted"), list);
        if (calls) {
            var number = new TextBox { Header = "Phone number", PlaceholderText = "+1 555 0100", MaxLength = 40, CornerRadius = new(12) };
            content.Children.Insert(2, g.Section(Stack(12, number, g.Button("Open dialer on phone", () => {
                var value = number.Text.Trim();
                if (value.Length is < 1 or > 40 || value.Any(c => !"0123456789+*# ()-".Contains(c))) { companionError = "Enter a phone number."; return; }
                inbox.Dial(value);
            }, true), g.Text("Tap the dial request notification on your phone to place the call. Notification sync and phone notification permission must be enabled.", 13, "muted"))));
        }
        RefreshNotifications(); return content;
    }
    private void RefreshNotifications() {
        var items = inbox.Items;
        var current = items.Select(n => n.Id).ToHashSet();
        foreach (var id in notificationCards.Keys.Where(id => !current.Contains(id)).ToArray()) notificationCards.Remove(id);
        void Fill(StackPanel? host, bool calls) {
            if (host == null) return;
            var desired = items.Where(n => n.Call == calls).Take(80).ToArray();
            var cards = new List<UIElement>();
            foreach (var n in desired) {
                if (!notificationCards.TryGetValue(n.Id, out var saved) || saved.Version != n.Version) {
                    var actions = Row(10);
                    foreach (var action in n.Actions) actions.Children.Add(g.Button(action.Label, () => {
                        if (action.Reply) Reply(n, action); else inbox.Action(n, action);
                    }, action.Reply));
                    actions.Children.Add(g.Button("Dismiss", () => inbox.Dismiss(n)));
                    var body = Stack(12, g.Text(n.App, 12, "muted"), g.Text(n.Title, 17, "text", true), g.Text(n.Text, 14, "muted"));
                    // Stack vertically at small window widths rather than overflowing action labels.
                    var actionWrap = Stack(8); foreach (var action in actions.Children.ToArray()) { actions.Children.Remove(action); ((FrameworkElement)action).HorizontalAlignment = HorizontalAlignment.Left; actionWrap.Children.Add(action); }
                    body.Children.Add(actionWrap); var card = g.Surface(body, "Subtle", 18); saved = (n.Version, card); notificationCards[n.Id] = saved;
                }
                cards.Add(saved.Card);
            }
            // Preserve existing elements, including keyboard focus, on unrelated notification updates.
            for (int i = host.Children.Count - 1; i >= 0; --i) if (!cards.Contains(host.Children[i])) host.Children.RemoveAt(i);
            for (int i = 0; i < cards.Count; ++i) {
                int at = host.Children.IndexOf(cards[i]); if (at == i) continue; if (at >= 0) host.Children.RemoveAt(at); host.Children.Insert(i, cards[i]);
            }
            if (cards.Count == 0 && host.Children.Count == 0) host.Children.Add(g.Text(calls ? "No active call notifications." : inbox.Enabled ? "You're all caught up." : "Waiting for notification access on your phone.", 14, "muted"));
        }
        Fill(notificationList, false); Fill(callList, true);
        foreach (var update in liveBindings.ToArray()) update();
    }
    private async void Reply(PhoneNotification notification, PhoneAction action) {
        var text = new TextBox { Header = "Your reply", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4096, MinHeight = 100, CornerRadius = new(14) };
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = $"Reply to {notification.Title}", Content = text, PrimaryButtonText = "Send", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(text.Text)) inbox.Action(notification, action, text.Text);
    }

    private StackPanel Settings()
    {
        return Stack(24, Heading("Make it yours", "Settings", "Appearance, comfort, and connection."),
            g.Section(Stack(20, g.Text("Appearance", 17, "text", true), g.Text("Dark glass", 14, "muted"), g.Dropdown("Visual effects quality", ["Balanced", "Economy", "High"], preferences.Effects, v => { preferences.Effects = v; preferences.Save(); Build(); }))), g.Rule(),
            g.Section(Stack(18, g.Text("Keep it close", 17, "text", true), g.Switch("Minimize to notification area", preferences.MinimizeToTray, v => { preferences.MinimizeToTray = v; preferences.Save(); }), g.Switch("Keep running when the window closes", preferences.CloseToTray, v => { preferences.CloseToTray = v; preferences.Save(); }), g.Text("Use the Synqora tray menu to open the window or quit. Pin Synqora to the taskbar for a quick launcher.", 13, "muted"))), g.Rule(),
            g.Section(Stack(20, g.Text("Comfort & accessibility", 17, "text", true), g.Switch("Reduce motion", preferences.ReducedMotion, v => { preferences.ReducedMotion = v; preferences.Save(); }), g.Switch("Reduce transparency", preferences.ReducedTransparency, v => { preferences.ReducedTransparency = v; preferences.Save(); Build(); }), g.Text("Windows animation, transparency, and high contrast preferences apply automatically.", 13, "muted"))), g.Rule(),
            g.Section(Stack(16, g.Text("Connection & privacy", 17, "text", true), Live(() => session.CanStream ? $"Encrypted local connection to {session.Device.Name}." : "No phone connected. Your microphone and camera are off.", 14, "muted"), ControlSwitch(), g.Button("Device details", ConnectionDetails))),
            g.Text("Synqora 0.5.0 preview", 12, "muted"));
    }
    private async Task CapturePreviews()
    {
        var index = Array.IndexOf(args, "--capture");
        var directory = index >= 0 && args.Length > index + 1 ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(directory);
        var originalTheme = preferences.Theme;
        var audits = new List<object>();
        foreach (var theme in new[] { "Dark" })
        {
            preferences.Theme = theme; Build(); await Task.Delay(300);
            foreach (var destination in pages)
            {
                Navigate(destination); await Task.Delay(450);
                AuditLayout(audits, theme, destination);
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
                var pixels = await bitmap.GetPixelsAsync();
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(directory));
                var target = await folder.CreateFileAsync($"windows-{theme.ToLowerInvariant()}-{destination.ToLowerInvariant()}.png", CreationCollisionOption.ReplaceExisting);
                using var stream = await target.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
            }
        }
        if (args.Contains("--resize-test")) {
            var originalSize = AppWindow.Size; var originalRoot = root;
            var retained = pageCache.ToDictionary(x => x.Key, x => x.Value);
            foreach (var size in new[] { new SizeInt32(720, 650), new SizeInt32(1920, 1080), new SizeInt32(1080, 820), new SizeInt32(2560, 1440) }) {
                AppWindow.Resize(size); await Task.Delay(160);
                foreach (var destination in pages) { Navigate(destination); await Task.Delay(60); AuditLayout(audits, "live-resize", destination); }
                if (!ReferenceEquals(root, originalRoot) || retained.Any(x => !ReferenceEquals(pageCache[x.Key], x.Value))) throw new InvalidOperationException("Resize rebuilt cached UI.");
            }
            AppWindow.Resize(originalSize);
        }
        File.WriteAllText(Path.Combine(directory, "windows-layout-audit.json"), JsonSerializer.Serialize(audits, new JsonSerializerOptions { WriteIndented = true }));
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
    private void AuditLayout(List<object> evidence, string scenario, string destination)
    {
        var violations = new List<string>(); var visited = 0;
        void Visit(DependencyObject node) {
            if (node is FrameworkElement element && element.Visibility == Visibility.Visible && element.ActualWidth > 0) {
                var p = element.TransformToVisual(pageHost).TransformPoint(new(0, 0));
                // Ignore internal templates whose adorners deliberately overhang controls.
                if (element is TextBlock or Button or StackPanel or Grid) {
                    visited++;
                    if (p.X < -1 || p.X + element.ActualWidth > pageHost.ActualWidth + 1) violations.Add($"{element.GetType().Name} {p.X:0.0}..{p.X + element.ActualWidth:0.0} / {pageHost.ActualWidth:0.0}");
                }
            }
            if (node is Button || node is Slider || node is ComboBox || node is ToggleSwitch) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
        Visit(pageHost);
        var selected = navButtons.Count(x => AutomationProperties.GetItemStatus(x.Value) == "Current page");
        evidence.Add(new { scenario, destination, width = root.ActualWidth, viewport = pageHost.ActualWidth, visited, selected, violations });
        if (violations.Count > 0 || selected != 1) throw new InvalidOperationException($"Layout audit failed at {root.ActualWidth}px {destination}: " + string.Join(", ", violations));
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
