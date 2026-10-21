package com.synqora

import android.Manifest
import android.animation.ValueAnimator
import android.content.*
import android.content.pm.PackageManager
import android.os.*
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalActivity
import androidx.activity.compose.setContent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.*
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.*
import androidx.compose.ui.draw.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.*
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.*
import androidx.compose.ui.unit.*
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import com.synqora.input.*
import androidx.lifecycle.*
import androidx.lifecycle.compose.*
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.viewmodel.compose.viewModel
import com.synqora.data.*
import com.synqora.design.*
import com.synqora.media.AudioMonitor
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    private var requestedDestination by mutableStateOf<String?>(null)
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); requestedDestination = intent.getStringExtra("destination")?.takeIf { it == "Files" || it == "Inbox" } }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (BuildConfig.DEBUG && intent.getBooleanExtra("reset_preferences", false)) getSharedPreferences("synqora", 0).edit().clear().commit()
        if (BuildConfig.DEBUG) intent.getStringExtra("effects")?.takeIf { it in listOf("Balanced", "Economy", "High") }?.let { getSharedPreferences("synqora", 0).edit().putString("effects", it).commit() }
        enableEdgeToEdge()
        requestedDestination = intent.getStringExtra("destination")?.takeIf { it == "Files" || it == "Inbox" }
        setContent {
            val state = (application as SynqoraApplication).state
            SynqoraApp(state, (if (BuildConfig.DEBUG) intent.getStringExtra("screen") else null) ?: requestedDestination, null)
        }
    }
}

@Composable private fun SynqoraApp(state: SynqoraState, initial: String?, themeOverride: String?) {
    val context = LocalContext.current
    val power = remember { context.getSystemService(PowerManager::class.java) }
    var saving by remember { mutableStateOf(power.isPowerSaveMode) }
    var thermal by remember { mutableIntStateOf(power.currentThermalStatus) }
    var animations by remember { mutableStateOf(ValueAnimator.areAnimatorsEnabled()) }
    DisposableEffect(context) {
        val receiver = object : BroadcastReceiver() { override fun onReceive(c: Context?, i: Intent?) { saving = power.isPowerSaveMode } }
        ContextCompat.registerReceiver(context, receiver, IntentFilter(PowerManager.ACTION_POWER_SAVE_MODE_CHANGED), ContextCompat.RECEIVER_NOT_EXPORTED)
        val listener = PowerManager.OnThermalStatusChangedListener { thermal = it }
        power.addThermalStatusListener(listener)
        val observer = object : android.database.ContentObserver(Handler(Looper.getMainLooper())) { override fun onChange(selfChange: Boolean) { animations = ValueAnimator.areAnimatorsEnabled() } }
        context.contentResolver.registerContentObserver(Settings.Global.getUriFor(Settings.Global.ANIMATOR_DURATION_SCALE), false, observer)
        onDispose { context.unregisterReceiver(receiver); power.removeThermalStatusListener(listener); context.contentResolver.unregisterContentObserver(observer) }
    }
    val activity = LocalActivity.current
    val dark = true
    SideEffect {
        activity?.window?.let { window ->
            WindowCompat.getInsetsController(window, window.decorView).apply {
                isAppearanceLightStatusBars = !dark
                isAppearanceLightNavigationBars = !dark
            }
        }
    }
    val renderPressure = rememberRenderPressure(state.effects)
    val economy = saving || thermal >= PowerManager.THERMAL_STATUS_MODERATE || state.effects == "Economy" || renderPressure
    val highContrast = Settings.Secure.getInt(context.contentResolver, "high_text_contrast_enabled", 0) == 1
    SynqoraTheme(dark, state.reduceTransparency || economy, animations && !state.reduceMotion && !economy, economy, highContrast, state.effects == "High") {
        Shell(state, initial)
    }
}

@Composable private fun Shell(state: SynqoraState, initial: String?) {
    val context = LocalContext.current
    val destinations = listOf("Overview", "Inbox", "Files", "Photos", "Calls", "Microphone", "Webcam", "Touchpad", "Keyboard", "Settings")
    var page by rememberSaveable { mutableStateOf(initial?.takeIf { it in destinations } ?: "Overview") }
    LaunchedEffect(initial) { initial?.takeIf { it in destinations }?.let { page = it } }
    var deviceDetails by remember { mutableStateOf(false) }
    var scanning by remember { mutableStateOf(false) }
    var pairingError by remember { mutableStateOf<String?>(null) }
    val c = LocalGlass.current
    val saved = rememberSaveableStateHolder()
    val device by state.session.device.collectAsStateWithLifecycle()
    val camera by state.camera.state.collectAsStateWithLifecycle()
    val scanPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted -> if (granted) { state.camera.stop(); scanning = true } else pairingError = "Camera permission is needed to scan. You can paste the pairing link instead." }
    val navigate: (String) -> Unit = { page = it }
    LaunchedEffect(device.phase) { if (device.phase == ConnectionPhase.Connected) { deviceDetails = false; scanning = false; pairingError = null } }
    BackHandler(page != "Overview") { page = when (page) { "Photos" -> "Files"; "Calls" -> "Inbox"; else -> "Overview" } }
    Box(Modifier.fillMaxSize().background(c["background"]).imePadding()) {
        Ambience()
        Column(Modifier.fillMaxSize().statusBarsPadding()) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 10.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (page !in listOf("Overview", "Inbox", "Files")) GlassIconButton(Icons.AutoMirrored.Outlined.ArrowBack, when (page) { "Photos" -> "Back to Files"; "Calls" -> "Back to Inbox"; else -> "Back to Device" }) { navigate(when (page) { "Photos" -> "Files"; "Calls" -> "Inbox"; else -> "Overview" }) }
                    if (page in listOf("Overview", "Inbox", "Files")) {
                        Image(painterResource(R.drawable.synqora_mark), null, Modifier.size(30.dp), colorFilter = ColorFilter.tint(c["accent"]))
                        GlassText("Synqora", 20, bold = true)
                    } else GlassText(page, 16, bold = true)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (page in listOf("Overview", "Inbox", "Files")) GlassIconButton(Icons.Outlined.Link, "Connection details") { deviceDetails = true }
                    GlassIconButton(Icons.Outlined.Tune, "Settings") { navigate("Settings") }
                }
            }
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.TopCenter) {
                val destination = page
                saved.SaveableStateProvider(destination) {
                    val scroll = rememberScrollState()
                    Column(Modifier.fillMaxHeight().widthIn(max = 680.dp).fillMaxWidth().verticalScroll(scroll).padding(horizontal = 24.dp).padding(top = 20.dp, bottom = 24.dp).then(if (destination in listOf("Overview", "Inbox", "Files")) Modifier else Modifier.navigationBarsPadding()), verticalArrangement = Arrangement.spacedBy(24.dp)) {
                        when (destination) {
                            "Overview" -> CompanionDashboard(device, state.microphoneRunning, camera.active, navigate) { deviceDetails = true }
                            "Inbox" -> CompanionInbox(state) { navigate("Calls") }
                            "Files" -> CompanionFiles(state) { navigate("Photos") }
                            "Photos" -> CompanionPhotos(state)
                            "Calls" -> CompanionCalls(state)
                            "Microphone" -> Microphone(state)
                            "Webcam" -> Webcam(state)
                            "Touchpad" -> Touchpad(state, device.phase == ConnectionPhase.Connected && device.controlsAllowed)
                            "Keyboard" -> Keyboard(state, device.phase == ConnectionPhase.Connected && device.controlsAllowed)
                            "Settings" -> SettingsScreen(state)
                        }
                    }
                }
            }
            if (page in listOf("Overview", "Inbox", "Files")) CompanionNavigation(page, Modifier.align(Alignment.CenterHorizontally).navigationBarsPadding().padding(horizontal = 24.dp, vertical = 12.dp), navigate)
        }
        if (state.displayDimmed) DimmedDisplay { state.dimDisplay(false) }
    }

    if (deviceDetails) GlassModal({ deviceDetails = false }, "Your connection") {
        var link by rememberSaveable { mutableStateOf("") }
        val keyboard = LocalSoftwareKeyboardController.current
        val connect = { if (link.isNotBlank()) { keyboard?.hide(); state.session.connect(link) } }
        if (device.phase == ConnectionPhase.Connected) {
            GlassStatusIndicator("Connected to ${device.name}", true)
            GlassText("${device.latencyMs ?: "—"} ms round trip · encrypted connection", 13, "muted")
            GlassText(if (device.controlsAllowed) "Remote controls are enabled on your PC." else "Enable remote controls on your PC to use the touchpad, and keyboard.", 14)
            GlassButton("Disconnect", Modifier.fillMaxWidth()) { state.stopMicrophone(); state.camera.stop(); state.session.disconnect() }
        } else {
            if (scanning) {
                GlassText("Point your phone at the code in Synqora on your PC.", 14, "muted")
                com.synqora.media.PairingScanner(Modifier.fillMaxWidth().height(280.dp).clip(RoundedCornerShape(24.dp)), { invitation -> scanning = false; state.session.connect(invitation) }, { pairingError = it; scanning = false })
                GlassButton("Paste a link instead", Modifier.fillMaxWidth()) { scanning = false }
            } else {
                GlassText("On your PC, open Connection details. Both devices must be on the same local network.", 14, "muted")
                GlassButton("Scan PC code", Modifier.fillMaxWidth(), true, icon = Icons.Outlined.QrCodeScanner) {
                    pairingError = null
                    if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) { state.camera.stop(); scanning = true } else scanPermission.launch(Manifest.permission.CAMERA)
                }
                OutlinedTextField(link, { link = it }, Modifier.fillMaxWidth(), label = { Text("Pairing link") }, singleLine = true, shape = RoundedCornerShape(16.dp), keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri, imeAction = androidx.compose.ui.text.input.ImeAction.Go), keyboardActions = androidx.compose.foundation.text.KeyboardActions(onGo = { connect() }))
                GlassButton("Connect", Modifier.fillMaxWidth(), enabled = link.isNotBlank(), loading = device.phase == ConnectionPhase.Connecting, icon = Icons.Outlined.Link, onClick = connect)
            }
        }
        (pairingError ?: device.error)?.let { GlassText(it, 13, "danger") }
    }
}

@Composable private fun DimmedDisplay(wake: () -> Unit) {
    val activity = LocalActivity.current
    BackHandler(onBack = wake)
    DisposableEffect(activity) {
        val window = activity?.window
        val oldBrightness = window?.attributes?.screenBrightness ?: -1f
        val wasKeptOn = window?.attributes?.flags?.and(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) != 0
        window?.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window?.attributes = window?.attributes?.apply { screenBrightness = .01f }
        onDispose {
            window?.attributes = window?.attributes?.apply { screenBrightness = oldBrightness }
            if (!wasKeptOn) window?.clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }
    Box(Modifier.fillMaxSize().background(Color.Black).pointerInput(Unit) { detectTapGestures(onDoubleTap = { wake() }) }.semantics { contentDescription = "Display dimmed. Double tap to return to camera controls."; onClick("Show camera controls") { wake(); true } }, contentAlignment = Alignment.Center) {
        Text("Double-tap to wake", color = Color(0xFF808080), fontSize = 14.sp)
    }
}


@Composable private fun Ambience() {
    val c = LocalGlass.current
    if (!c.solid) Canvas(Modifier.fillMaxSize()) {
        drawRect(Brush.verticalGradient(listOf(c["ambience"].copy(alpha = .32f), Color.Transparent, c["background"])))
    }
}

@Composable private fun Heading(eyebrow: String, title: String, subtitle: String) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        GlassDisplay(title, 30, modifier = Modifier.semantics { heading() })
        GlassText(subtitle, 14, "muted")
    }
}

@Composable private fun ColumnScope.Microphone(state: SynqoraState) {
    val context = LocalContext.current
    val monitor = state.audio
    val running = state.microphoneRunning
    val device by state.session.device.collectAsStateWithLifecycle()
    var permissionError by remember { mutableStateOf<String?>(null) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted -> if (granted) state.startMicrophone() else permissionError = "Allow microphone access in Android settings to use your microphone." }
    Heading("", "Microphone", "Phone audio, ready for your PC.")
    Column(Modifier.fillMaxWidth().padding(vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            GlassText("Input monitor", 16, bold = true)
            GlassStatusIndicator(if (running && state.muted) "Muted" else if (running) "Live" else "Off", running && !state.muted)
        }
        InputWaveform(monitor, state.muted, state.gain)
        GlassText(if (running && device.phase == ConnectionPhase.Connected) "Streaming to ${device.name}" else if (running) "Listening on this phone" else "Ready when you are", 20, bold = true)
        GlassText(if (device.phase == ConnectionPhase.Connected) "Your audio goes directly to your PC." else "Connect your PC before starting the microphone.", 13, "muted")
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            GlassButton(if (running) "Stop" else "Start microphone", Modifier.weight(1f), true, icon = if (running) Icons.Outlined.Stop else Icons.Outlined.Mic) {
                if (running) state.stopMicrophone()
                else if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) state.startMicrophone()
                else permission.launch(Manifest.permission.RECORD_AUDIO)
            }
            GlassIconButton(if (state.muted) Icons.Outlined.MicOff else Icons.Outlined.Mic, if (state.muted) "Unmute" else "Mute", onClick = state::mute)
        }
        (permissionError ?: state.microphoneError ?: device.error)?.let { GlassText(it, 13, "danger", modifier = Modifier.semantics { liveRegion = LiveRegionMode.Assertive }) }
    }
    Rule()
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) { GlassSlider("Input gain", state.gain, state::gain); GlassText("48 kHz · 16-bit · mono", 12, "muted") }
    Rule()
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) { GlassIcon(Icons.Outlined.LaptopWindows, size = 20.dp); GlassText("Listen in your PC apps", 16, bold = true) }
        GlassText("In Synqora on Windows, complete microphone setup. Then choose the recording device shown there in Meet or your audio app.", 13, "muted")
        if (device.phase == ConnectionPhase.Connected) GlassStatusIndicator(if (device.receivedAudioBytes > 0) "Audio received · ${device.latencyMs ?: "—"} ms" else "Waiting for audio on PC", device.receivedAudioBytes > 0)
    }
}

@Composable private fun InputWaveform(monitor: AudioMonitor, muted: Boolean, gain: Float) {
    val levels by monitor.levels.collectAsStateWithLifecycle()
    GlassAudioVisualizer(if (muted) List(64) { 0f } else levels.map { (it * gain).coerceAtMost(1f) })
}

@Composable private fun ColumnScope.Webcam(state: SynqoraState) {
    val context = LocalContext.current
    val capture by state.camera.state.collectAsStateWithLifecycle()
    val preview = capture.active
    var error by remember { mutableStateOf<String?>(null) }
    var tools by remember { mutableStateOf(true) }
    val device by state.session.device.collectAsStateWithLifecycle()
    val microphonePermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { if (it) state.startMicrophone() else error = "Microphone permission is required to include audio." }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { if (it) state.startCamera() else error = "Allow camera access in Android settings to use your camera." }
    val viewport = LocalConfiguration.current
    val landscape = viewport.screenWidthDp > viewport.screenHeightDp
    if (landscape) GlassDisplay("Webcam", 26)
    else Heading("", "Webcam", "Your phone camera, on your PC.")
    val c = LocalGlass.current
    val previewHeight = (viewport.screenHeightDp * if (landscape) .38f else .44f).coerceAtLeast(150f).dp
    Box(Modifier.fillMaxWidth().height(previewHeight).clip(RoundedCornerShape(28.dp)).background(c["background"]).border(1.dp, c["border"].copy(alpha = .6f), RoundedCornerShape(28.dp))) {
        if (preview) CameraPreview(Modifier.fillMaxSize(), state.camera)
        else if (landscape) Box(Modifier.align(Alignment.TopCenter).padding(top = 44.dp)) { GlassIcon(Icons.Outlined.Videocam, size = 32.dp) }
        else Column(Modifier.align(Alignment.Center).padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) { GlassIcon(Icons.Outlined.Videocam, size = 36.dp); GlassText("Ready when you are", 22, bold = true); GlassText("Your camera is off.", 14, "muted") }
        Box(Modifier.align(Alignment.TopStart).padding(16.dp)) { GlassStatusIndicator(if (preview && !capture.ready) "Starting camera" else if (preview && device.phase == ConnectionPhase.Connected) "Streaming to PC" else if (preview) "Local preview only" else "Camera off", preview && capture.ready) }
        Box(Modifier.align(Alignment.TopEnd).padding(10.dp)) { GlassIconButton(if (tools) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility, if (tools) "Hide controls" else "Show controls") { tools = !tools } }
        androidx.compose.animation.AnimatedVisibility(tools, Modifier.align(Alignment.BottomCenter).padding(12.dp), enter = if (c.motion) fadeIn(tween(180)) + slideInVertically(spring(.9f, 420f)) { it / 5 } else EnterTransition.None, exit = if (c.motion) fadeOut(tween(120)) else ExitTransition.None) {
            GlassSurface(material = "Elevated", padding = 6.dp, radius = 32.dp) {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    GlassIconButton(Icons.Outlined.Cameraswitch, "Flip camera") { state.camera.flip() }
                    GlassButton(if (preview) "Stop" else "Start", Modifier.semantics { contentDescription = if (preview) "Stop camera" else "Start camera" }, primary = true, enabled = preview || device.phase == ConnectionPhase.Connected, icon = if (preview) Icons.Outlined.Stop else Icons.Outlined.Videocam) { if (preview) state.camera.stop() else if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) state.startCamera() else permission.launch(Manifest.permission.CAMERA) }
                    GlassIconButton(Icons.Outlined.BrightnessLow, "Dim display", enabled = preview) { state.dimDisplay(true) }
                }
            }
        }
    }
    var cameraMode by remember { mutableStateOf(state.camera.mode) }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(18.dp)) {
        GlassDropdown("Video quality", listOf("Smooth · 60 fps", "Detail · 30 fps", "Economy · 60 fps"), cameraMode) { cameraMode = it; state.camera.mode(it) }
        GlassSwitch("Include microphone", "Send audio with your camera", state.microphoneRunning) { enabled ->
            if (!enabled) state.stopMicrophone()
            else if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) state.startMicrophone()
            else microphonePermission.launch(Manifest.permission.RECORD_AUDIO)
        }
    }
    (error ?: capture.error)?.let { GlassText(it, 13, "danger") }
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        GlassText(if (preview && device.phase == ConnectionPhase.Connected) "PC receiving ${device.receivedVideoFps} fps" else "Camera goes directly to your PC.", 13, "muted")
        if (preview && capture.ready) GlassText("${capture.width} × ${capture.height} · phone ${capture.actualFps} fps · target ${capture.targetFps}", 12, "muted")
        GlassText("60 fps depends on camera support, light and network capacity.", 12, "muted")
        device.error?.let { GlassText(it, 13, "danger") }
    }
}
@Composable private fun CameraPreview(modifier: Modifier, capture: com.synqora.media.CameraCapture) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current
    val view = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER; implementationMode = PreviewView.ImplementationMode.PERFORMANCE } }
    DisposableEffect(view, capture, lifecycle) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_START) capture.attachPreview(view)
            if (event == Lifecycle.Event.ON_STOP) capture.detachPreview(view)
        }
        lifecycle.lifecycle.addObserver(observer)
        if (lifecycle.lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) capture.attachPreview(view)
        onDispose { lifecycle.lifecycle.removeObserver(observer); capture.detachPreview(view) }
    }
    AndroidView({ view }, modifier)
}

@Composable private fun ColumnScope.Touchpad(state: SynqoraState, connected: Boolean) {
    val c = LocalGlass.current
    var keyboard by remember { mutableStateOf(false) }
    Heading("", "Touchpad", "One finger to move. Two fingers to scroll.")
    val height = (LocalConfiguration.current.screenHeightDp * .38f).coerceIn(200f, 380f).dp
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).border(1.dp, c["text"].copy(alpha = .1f), RoundedCornerShape(20.dp)).padding(4.dp)) {
        AndroidView(factory = { RawTouchpadView(it).apply { session = state.session } }, modifier = Modifier.fillMaxWidth().height(height).semantics { contentDescription = "Remote touchpad" }, update = { it.ready = connected; it.dark = c.dark; it.haptics = state.haptics })
    }
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        GlassButton("Left click", Modifier.weight(1f), enabled = connected) { state.session.click(false) }
        GlassButton("Right click", Modifier.weight(1f), enabled = connected) { state.session.click(true) }
        GlassIconButton(Icons.Outlined.Keyboard, "Open live keyboard", connected) { keyboard = !keyboard }
    }
    if (keyboard) LiveKeyboardInput(state, connected, true)
    GlassStatusIndicator(if (connected) "Remote input connected" else "Allow remote input on your PC", connected)
    Rule()
    GlassSwitch("Touch feedback", "Light haptics when you click", state.haptics, state::haptics)
}

@Composable private fun LiveKeyboardInput(state: SynqoraState, connected: Boolean, focus: Boolean, attach: (LiveKeyboardView) -> Unit = {}) {
    val c = LocalGlass.current
    AndroidView(factory = { LiveKeyboardView(it).apply { session = state.session; attach(this) } }, modifier = Modifier.fillMaxWidth().height(76.dp).semantics { contentDescription = "Live keyboard" }, update = { it.dark = c.dark; it.configure(connected, focus) })
}
@Composable private fun ColumnScope.Keyboard(state: SynqoraState, connected: Boolean) {
    var input by remember { mutableStateOf<LiveKeyboardView?>(null) }
    Heading("", "Keyboard", "Type here. It appears on your PC immediately.")
    LiveKeyboardInput(state, connected, true) { input = it }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        GlassButton("⌫", Modifier.weight(1f).semantics { contentDescription = "Backspace" }, enabled = connected) { input?.sendKey("backspace") }
        GlassButton("Tab", Modifier.weight(1f), enabled = connected) { input?.sendKey("tab") }
        GlassButton("Enter", Modifier.weight(1f), enabled = connected) { input?.sendKey("enter") }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf("←" to "left", "↑" to "up", "↓" to "down", "→" to "right").forEach { (label, key) -> GlassButton(label, Modifier.weight(1f), enabled = connected) { input?.sendKey(key) } }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf("Copy" to "c", "Paste" to "v", "Undo" to "z").forEach { (label, key) -> GlassButton(label, Modifier.weight(1f), enabled = connected) { input?.sendKey(key, 2) } }
        GlassButton("Esc", Modifier.weight(1f), enabled = connected) { input?.sendKey("escape") }
    }
    GlassStatusIndicator(if (connected) "Live keyboard connected" else "Allow remote input on your PC", connected)
}

@Composable private fun ColumnScope.SettingsScreen(state: SynqoraState) {
    Heading("", "Settings", "Make Synqora feel right for you.")
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) { GlassIcon(Icons.Outlined.Palette); GlassText("Appearance", 20, bold = true) }
        GlassDropdown("Visual effects quality", listOf("Balanced", "Economy", "High"), state.effects, state::effects)
    }
    Rule()
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(20.dp)) {
        GlassText("Comfort & accessibility", 20, bold = true)
        GlassSwitch("Reduce motion", "Calmer, immediate transitions", state.reduceMotion, state::motion)
        GlassSwitch("Reduce transparency", "Solid surfaces with clear contrast", state.reduceTransparency, state::transparency)
        GlassText(if (LocalGlass.current.economy) "Effects are reduced to keep controls responsive." else "Animation settings, battery saver, thermal pressure and sustained rendering stress reduce decorative effects automatically.", 12, "muted")
    }
    val device by state.session.device.collectAsStateWithLifecycle()
    Rule()
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) { GlassText("Connection & privacy", 18, bold = true); GlassText(if (device.phase == ConnectionPhase.Connected) "Encrypted local connection to ${device.name}. Your foreground session continues with the screen locked. Use its notification to disconnect." else "No computer connected. Capture asks for permission when you start it and stays visible in an active session notification.", 13, "muted") }
    GlassText("Synqora 0.5.0 preview", 12, "muted")
}
@Composable private fun Rule() = HorizontalDivider(color = LocalGlass.current["text"].copy(alpha = .08f))
@Composable private fun Metric(label: String, value: String) { Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { GlassText(label, 10, "muted", true); GlassText(value, 22, bold = true) } }
