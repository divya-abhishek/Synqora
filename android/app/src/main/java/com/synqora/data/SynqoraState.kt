package com.synqora.data

import android.app.Application
import androidx.compose.runtime.*
import androidx.lifecycle.AndroidViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.*
import com.synqora.SessionService
import androidx.lifecycle.viewModelScope
import com.synqora.media.AudioMonitor
import com.synqora.media.CameraCapture
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

enum class ConnectionPhase { Disconnected, Connecting, Connected, Error }
data class DeviceState(val name: String = "Your computer", val identity: String = "No computer paired", val phase: ConnectionPhase = ConnectionPhase.Disconnected, val latencyMs: Int? = null, val battery: Int? = null, val controlsAllowed: Boolean = false, val error: String? = null, val receivedAudioBytes: Long = 0, val receivedVideoFrames: Long = 0, val receivedVideoFps: Int = 0)
data class SessionStatus(val phase: ConnectionPhase = ConnectionPhase.Disconnected, val name: String = "Your computer", val microphone: Boolean = false, val camera: Boolean = false) {
    val keepAlive get() = microphone || camera || phase == ConnectionPhase.Connecting || phase == ConnectionPhase.Connected
}
interface SynqoraSession {
    val device: StateFlow<DeviceState>
    fun connect(link: String)
    fun disconnect()
    fun microphone(active: Boolean)
    fun camera(active: Boolean)
    fun audio(bytes: ByteArray)
    fun video(bytes: ByteArray, rotation: Int = 0)
    fun pointer(dx: Float, dy: Float)
    fun scroll(delta: Float)
    fun click(right: Boolean)
    fun text(value: String)
    fun key(value: String, modifiers: Int = 0)
    fun edit(move: Int, before: Int, after: Int, text: String)
    fun media(command: String)
    fun volume(level: Float)
}
class UnconfiguredSession : SynqoraSession {
    override val device = MutableStateFlow(DeviceState())
    override fun pointer(dx: Float, dy: Float) = Unit
    override fun connect(link: String) = Unit
    override fun disconnect() = Unit
    override fun microphone(active: Boolean) = Unit
    override fun camera(active: Boolean) = Unit
    override fun audio(bytes: ByteArray) = Unit
    override fun video(bytes: ByteArray, rotation: Int) = Unit
    override fun scroll(delta: Float) = Unit
    override fun click(right: Boolean) = Unit
    override fun text(value: String) = Unit
    override fun key(value: String, modifiers: Int) = Unit
    override fun edit(move: Int, before: Int, after: Int, text: String) = Unit
    override fun media(command: String) = Unit
    override fun volume(level: Float) = Unit
}
class SynqoraState(private val app: Application) : AndroidViewModel(app) {
    private val prefs = app.getSharedPreferences("synqora", 0)
    val session: SynqoraSession = LanSession(beforeConnect = { SessionService.ensure(app) }) { app.getSystemService(android.os.BatteryManager::class.java).getIntProperty(android.os.BatteryManager.BATTERY_PROPERTY_CAPACITY).takeIf { it in 0..100 } }
    val audio = AudioMonitor()
    val camera = CameraCapture(app, session::video) { SessionService.ensure(app, camera = true, microphone = microphoneRunning) }
    var microphoneRunning by mutableStateOf(false); private set
    var microphoneError by mutableStateOf<String?>(null); private set
    var displayDimmed by mutableStateOf(false); private set
    fun dimDisplay(value: Boolean) { displayDimmed = value }
    val status = combine(session.device.map { it.phase to it.name }.distinctUntilChanged(), camera.state.map { it.active }.distinctUntilChanged(), snapshotFlow { microphoneRunning }) { device, cam, mic -> SessionStatus(device.first, device.second, mic, cam) }.stateIn(viewModelScope, SharingStarted.Eagerly, SessionStatus())
    var theme by mutableStateOf(prefs.getString("theme", "System")!!); private set
    var reduceMotion by mutableStateOf(prefs.getBoolean("motion", false)); private set
    var reduceTransparency by mutableStateOf(prefs.getBoolean("transparency", false)); private set
    var effects by mutableStateOf(prefs.getString("effects", "Balanced")!!); private set
    var haptics by mutableStateOf(prefs.getBoolean("haptics", true)); private set
    var gain by mutableFloatStateOf(prefs.getFloat("gain", .7f)); private set
    var volume by mutableFloatStateOf(prefs.getFloat("volume", .5f)); private set
    var quality by mutableStateOf(prefs.getString("quality", "Studio · 48 kHz")!!); private set
    var muted by mutableStateOf(false); private set
    private val captureTransition = Mutex()
    private var captureGeneration = 0
    init {
        viewModelScope.launch {
            var sendingMic = false; var sendingCamera = false
            combine(session.device.map { it.phase == ConnectionPhase.Connected }.distinctUntilChanged(), camera.state.map { it.active && it.ready }.distinctUntilChanged(), snapshotFlow { microphoneRunning }) { connected, cam, mic -> (connected && mic) to (connected && cam) }.collect { (mic, cam) ->
                if (mic != sendingMic) { sendingMic = mic; session.microphone(mic) }
                if (cam != sendingCamera) { sendingCamera = cam; session.camera(cam) }
                camera.transmit = cam
            }
        }
        viewModelScope.launch { camera.state.collect { if (!it.active) displayDimmed = false } }
    }
    fun mute() { muted = !muted; audio.muted = muted }
    fun startMicrophone() {
        SessionService.ensure(app, microphone = true, camera = camera.state.value.active)
        val generation = ++captureGeneration
        microphoneError = null; microphoneRunning = true; audio.gain = gain
        viewModelScope.launch { captureTransition.withLock {
            if (generation != captureGeneration) return@withLock
            audio.stop()
            if (generation != captureGeneration) return@withLock
            session.microphone(true)
            audio.start(viewModelScope, { microphoneError = it; microphoneRunning = false; session.microphone(false) }, session::audio)
        } }
    }
    fun stopMicrophone() {
        val generation = ++captureGeneration
        microphoneRunning = false; session.microphone(false)
        viewModelScope.launch { captureTransition.withLock { if (generation == captureGeneration) audio.stop() } }
    }
    fun theme(value: String) { theme = value; prefs.edit().putString("theme", value).apply() }
    fun motion(value: Boolean) { reduceMotion = value; prefs.edit().putBoolean("motion", value).apply() }
    fun transparency(value: Boolean) { reduceTransparency = value; prefs.edit().putBoolean("transparency", value).apply() }
    fun effects(value: String) { effects = value; prefs.edit().putString("effects", value).apply() }
    fun haptics(value: Boolean) { haptics = value; prefs.edit().putBoolean("haptics", value).apply() }
    fun gain(value: Float) { gain = value; audio.gain = value; prefs.edit().putFloat("gain", value).apply() }
    fun volume(value: Float) { volume = value; prefs.edit().putFloat("volume", value).apply(); session.volume(value) }
    fun quality(value: String) { quality = value; prefs.edit().putString("quality", value).apply() }
    override fun onCleared() { camera.close(); (session as? LanSession)?.close(); super.onCleared() }
}
