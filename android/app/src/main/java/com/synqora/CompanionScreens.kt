package com.synqora

import android.content.ComponentName
import android.content.Intent
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.Reply
import androidx.compose.material.icons.automirrored.outlined.DriveFileMove
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.*
import androidx.compose.ui.unit.*
import androidx.core.app.NotificationManagerCompat
import android.os.Build
import android.Manifest
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.synqora.data.*
import com.synqora.design.*

@Composable internal fun CompanionNavigation(page: String, modifier: Modifier, navigate: (String) -> Unit) {
    val c = LocalGlass.current
    GlassSurface(modifier.widthIn(max = 420.dp).fillMaxWidth(), material = "Elevated", padding = 6.dp, radius = 32.dp) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            listOf(Triple("Overview", "Device", Icons.Outlined.Devices), Triple("Inbox", "Inbox", Icons.Outlined.NotificationsNone), Triple("Files", "Files", Icons.Outlined.FolderOpen)).forEach { (destination, label, icon) ->
                val source = remember { MutableInteractionSource() }; val selected = page == destination
                TextButton({ navigate(destination) }, Modifier.weight(1f).heightIn(min = 58.dp).glassPress(source).semantics { this.selected = selected }, interactionSource = source, shape = RoundedCornerShape(26.dp), colors = ButtonDefaults.textButtonColors(contentColor = c[if (selected) "text" else "muted"], containerColor = if (selected) c["text"].copy(alpha = .1f) else Color.Transparent), contentPadding = PaddingValues(6.dp)) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(3.dp)) { Icon(icon, null, Modifier.size(22.dp)); Text(label, fontSize = 12.sp) }
                }
            }
        }
    }
}
@Composable internal fun ColumnScope.CompanionDashboard(device: DeviceState, mic: Boolean, camera: Boolean, navigate: (String) -> Unit, connect: () -> Unit) {
    val connected = device.phase == ConnectionPhase.Connected
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) { GlassDisplay("Your devices", 34); GlassText("Connect your phone and PC.", 14, "muted") }
    GlassSurface(Modifier.fillMaxWidth(), "Regular", 24.dp, 28.dp) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(52.dp).clip(RoundedCornerShape(18.dp)).background(LocalGlass.current["text"].copy(alpha = .07f)), contentAlignment = Alignment.Center) { GlassIcon(Icons.Outlined.LaptopWindows, size = 28.dp) }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) { GlassText(if (connected) device.name else "Your computer", 20, bold = true); GlassText(if (connected) "Connected securely" else "Not connected", 13, if (connected) "success" else "muted") }
        }
        if (connected) {
            HorizontalDivider(color = LocalGlass.current["text"].copy(alpha = .08f))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) { GlassText(if (mic || camera) "Sharing ${listOfNotNull(if (camera) "camera" else null, if (mic) "audio" else null).joinToString(" + ")}" else "Companion ready", 14, bold = true); GlassText(if (mic || camera) "Tap a control to stop sharing" else "Camera & microphone are off", 12, "muted") }
                GlassIcon(Icons.Outlined.CheckCircleOutline, size = 24.dp, token = "success")
            }
        } else {
            GlassText("Connect over your local network. Your files and notifications go directly to your PC.", 14, "muted")
            GlassButton(if (device.phase == ConnectionPhase.Connecting) "Connecting" else "Connect a computer", Modifier.fillMaxWidth(), true, loading = device.phase == ConnectionPhase.Connecting, icon = Icons.Outlined.QrCodeScanner, onClick = connect)
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        GlassText("Use your phone", 18, bold = true)
        val controls = listOf(Triple("Microphone", if (mic) "Sharing audio" else "Clearer conversations", Icons.Outlined.MicNone), Triple("Webcam", if (camera) "Camera is live" else "A better camera", Icons.Outlined.Videocam), Triple("Touchpad", "A little more control", Icons.Outlined.TouchApp), Triple("Keyboard", "Type from here", Icons.Outlined.Keyboard))
        controls.chunked(2).forEach { row -> Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            row.forEach { (name, subtitle, icon) ->
                val source = remember { MutableInteractionSource() }
                GlassSurface(Modifier.weight(1f).glassPress(source).clickable(source, null, role = Role.Button) { navigate(name) }, "Subtle", 18.dp, 22.dp) {
                    GlassIcon(icon, size = 26.dp)
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) { GlassText(name, 16, bold = true); GlassText(subtitle, 12, "muted") }
                }
            }
        } }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) { GlassIcon(Icons.Outlined.Lock, size = 14.dp, token = "muted"); GlassText("Encrypted local connection", 12, "muted") }
    device.error?.let { GlassText(it, 13, "danger") }
}

@Composable internal fun ColumnScope.CompanionInbox(state: SynqoraState, calls: () -> Unit) {
    val context = LocalContext.current
    val syncing by state.companion.syncEnabled.collectAsStateWithLifecycle()
    var granted by remember { mutableStateOf(NotificationManagerCompat.getEnabledListenerPackages(context).contains(context.packageName)) }
    var alerts by remember { mutableStateOf(NotificationManagerCompat.from(context).areNotificationsEnabled()) }
    val requestAlerts = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { alerts = NotificationManagerCompat.from(context).areNotificationsEnabled() }
    androidx.lifecycle.compose.LifecycleResumeEffect(Unit) { granted = NotificationManagerCompat.getEnabledListenerPackages(context).contains(context.packageName); alerts = NotificationManagerCompat.from(context).areNotificationsEnabled(); onPauseOrDispose { } }
    GlassDisplay("Notifications", 32)
    GlassText("Notifications and replies, right on your computer.", 15, "muted")
    GlassButton("Call settings", Modifier.fillMaxWidth(), icon = Icons.Outlined.Call, onClick = calls)
    GlassSurface(Modifier.fillMaxWidth(), "Regular", radius = 26.dp) {
        GlassIcon(Icons.Outlined.Forum, size = 32.dp)
        GlassSwitch("Sync notifications", "Share with your connected PC only.", syncing, state.companion::sync)
        if (!granted) {
            GlassText("Android needs your approval to read notifications and use their reply actions. Notifications marked secret are excluded.", 14, "muted")
            GlassButton("Choose notification access", Modifier.fillMaxWidth(), true) { context.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) }
        } else GlassStatusIndicator(if (syncing) "Notification sync enabled" else "Access granted · sync paused", syncing)
        if (!alerts) GlassButton("Allow transfer and dial alerts", Modifier.fillMaxWidth()) {
            if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) requestAlerts.launch(Manifest.permission.POST_NOTIFICATIONS)
            else context.startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName))
        }
    }
    InfoRow(Icons.AutoMirrored.Outlined.Reply, "Reply without reaching", "WhatsApp, SMS, RCS and other apps work when their notification includes a reply action.")
    InfoRow(Icons.Outlined.Call, "Calls stay on your phone", "Call notifications appear on your PC. Available answer or decline actions depend on the calling app. Call audio is not routed to the PC.")
    InfoRow(Icons.Outlined.BatterySaver, "Quiet when idle", "No camera, microphone or CPU wake lock runs just to sync notifications. Android may defer delivery while your phone is asleep.")
}

@Composable private fun InfoRow(icon: ImageVector, title: String, detail: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) { GlassIcon(icon, size = 23.dp, token = "muted"); Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) { GlassText(title, 16, bold = true); GlassText(detail, 14, "muted") } }
}

@Composable internal fun ColumnScope.CompanionFiles(state: SynqoraState, photos: () -> Unit) {
    val files by state.companion.transfers.collectAsStateWithLifecycle()
    val device by state.session.device.collectAsStateWithLifecycle()
    val message by state.companion.message.collectAsStateWithLifecycle()
    var accepting by rememberSaveable { mutableStateOf<String?>(null) }
    val choose = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> uri?.let { state.companion.offer(it) } }
    val save = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri -> accepting?.let { id -> if (uri != null) state.companion.accept(id, uri) }; accepting = null }
    GlassDisplay("Files", 32)
    GlassText("Send files between your phone and PC.", 15, "muted")
    GlassButton("Photos and videos", Modifier.fillMaxWidth(), icon = Icons.Outlined.PhotoLibrary, onClick = photos)
    GlassSurface(Modifier.fillMaxWidth(), "Regular", radius = 26.dp) {
        GlassIcon(Icons.AutoMirrored.Outlined.DriveFileMove, size = 32.dp)
        GlassText("Share with your computer", 20, bold = true)
        GlassText("Share files of any size, within available storage. Every transfer is encrypted and checked before it is saved.", 14, "muted")
        GlassButton("Choose a file", Modifier.fillMaxWidth(), true, enabled = device.phase == ConnectionPhase.Connected, icon = Icons.Outlined.Add) { state.companion.message.value = null; choose.launch(arrayOf("*/*")) }
        if (device.phase != ConnectionPhase.Connected) GlassText("Connect a computer to send or receive.", 13, "muted")
    }
    if (files.isNotEmpty()) GlassText("Transfers", 18, bold = true)
    files.take(30).forEach { file -> key(file.id) { GlassSurface(Modifier.fillMaxWidth(), "Subtle", 18.dp, 20.dp) {
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            GlassIcon(if (file.incoming) Icons.Outlined.SouthWest else Icons.Outlined.NorthEast, size = 22.dp)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) { GlassText(file.name, 15, bold = true); GlassText("${fileSize(file.size)} · ${if (file.incoming) "From PC" else "To PC"} · ${file.status}", 12, "muted") }
        }
        if (file.status == "Transferring") LinearProgressIndicator(progress = { if (file.size == 0L) 1f else file.done.toFloat() / file.size }, modifier = Modifier.fillMaxWidth(), color = LocalGlass.current["accent"], trackColor = LocalGlass.current["text"].copy(alpha = .1f))
        if (file.incoming && file.status == "Waiting") Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            GlassButton("Save", primary = true) { accepting = file.id; save.launch(file.name) }
            GlassButton("Decline") { state.companion.cancel(file.id) }
        } else if (file.status in listOf("Waiting", "Ready", "Transferring")) GlassButton("Cancel") { state.companion.cancel(file.id) }
    } } }
    message?.let { GlassText(it, 14, "danger") }
}

@Composable internal fun ColumnScope.CompanionPhotos(state: SynqoraState) {
    val context = LocalContext.current
    val sharing by state.gallery.enabled.collectAsStateWithLifecycle()
    val access by state.gallery.access.collectAsStateWithLifecycle()
    val permissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { state.gallery.refreshAccess() }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(12)) { selected -> selected.forEach { state.companion.offer(it) } }
    fun request() { permissions.launch(when {
        Build.VERSION.SDK_INT >= 34 -> arrayOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO, Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED)
        Build.VERSION.SDK_INT >= 33 -> arrayOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
        else -> arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
    }) }
    androidx.lifecycle.compose.LifecycleResumeEffect(Unit) { state.gallery.refreshAccess(); onPauseOrDispose { } }
    GlassDisplay("Photos and videos", 30)
    GlassText("Browse your phone gallery from your PC. Save originals when you need them.", 15, "muted")
    GlassSurface(Modifier.fillMaxWidth(), "Regular", radius = 26.dp) {
        GlassIcon(Icons.Outlined.PhotoLibrary, size = 32.dp)
        GlassSwitch("Share gallery with PC", "Only your connected PC can browse.", sharing) { state.gallery.sharing(it); if (it && !state.gallery.hasAccess()) request() }
        GlassStatusIndicator(access, sharing && state.gallery.hasAccess())
        if (sharing) GlassButton("Choose photo access", Modifier.fillMaxWidth()) { request() }
        GlassText("Choose selected photos on supported Android versions, or allow your library. Turn sharing off to remove gallery access from your PC.", 14, "muted")
    }
    GlassSurface(Modifier.fillMaxWidth(), "Subtle", radius = 24.dp) {
        GlassText("Share a few instead", 18, bold = true)
        GlassText("Send selected photos or videos without sharing your gallery.", 14, "muted")
        GlassButton("Choose photos and videos", Modifier.fillMaxWidth()) { picker.launch(androidx.activity.result.PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageAndVideo)) }
    }
    InfoRow(Icons.Outlined.BatterySaver, "No continuous scanning", "Thumbnails load on demand. Gallery changes are event driven; your camera stays off. Original files transfer only when you choose them.")
}
internal fun fileSize(bytes: Long) = if (bytes < 1024 * 1024) "${(bytes / 1024).coerceAtLeast(1)} KB" else "${bytes / (1024 * 1024)} MB"

@Composable internal fun ColumnScope.CompanionCalls(state: SynqoraState) {
    val history by state.calls.history.collectAsStateWithLifecycle()
    val dialing by state.calls.dialing.collectAsStateWithLifecycle()
    val access by state.calls.access.collectAsStateWithLifecycle()
    val request = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { state.calls.refreshAccess() }
    androidx.lifecycle.compose.LifecycleResumeEffect(Unit) { state.calls.refreshAccess(); onPauseOrDispose { } }
    GlassDisplay("Call settings", 30)
    GlassText("Choose what your connected PC can access.", 15, "muted")
    GlassSurface(Modifier.fillMaxWidth(), "Regular", radius = 26.dp) {
        GlassSwitch("Share call history", "Recent cellular calls appear on your PC.", history) { state.calls.history(it); if (it && !state.calls.granted(Manifest.permission.READ_CALL_LOG)) request.launch(Manifest.permission.READ_CALL_LOG) }
        GlassSwitch("Allow calls from PC", "Your PC can place a cellular call using this phone.", dialing) { state.calls.dialing(it); if (it && !state.calls.granted(Manifest.permission.CALL_PHONE)) request.launch(Manifest.permission.CALL_PHONE) }
        GlassText(access, 13, "muted")
        if (history && !state.calls.granted(Manifest.permission.READ_CALL_LOG)) GlassText("Android or the app installer may restrict call-history access on this phone. History stays unavailable until permission is granted.", 13, "muted")
    }
    InfoRow(Icons.Outlined.Bluetooth, "Bluetooth audio", "PC audio requires the experimental Windows calling MSIX and a paired Bluetooth phone. Connect it from Calls on your PC. Hardware compatibility is still being tested.")
    InfoRow(Icons.Outlined.Call, "WhatsApp", "Call notifications can provide answer and decline actions. WhatsApp does not expose its private call history or direct calling to this app.")
}
