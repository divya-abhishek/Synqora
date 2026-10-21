package com.synqora.data

import android.net.Uri
import android.os.Build
import android.os.SystemClock
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.*

class LanSession(private val beforeConnect: () -> Unit = {}, private val batteryLevel: () -> Int? = { null }) : SynqoraSession {
    override val device = MutableStateFlow(DeviceState())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val lifecycle = Mutex()
    private val laneGate = Any()
    private var connection: Job? = null
    private var socket: SSLSocket? = null
    private var audioLane: Lane? = null
    private var videoLane: Lane? = null
    private var control = Channel<Packet>(128)
    @Volatile private var microphone = false
    @Volatile private var camera = false
    @Volatile private var latency: Int? = null
    private var address: Pairing? = null
    private var sessionId = ""
    private data class Packet(val kind: Int, val payload: ByteArray)
    private data class Pairing(val host: String, val port: Int, val pin: String, val key: String)
    private inner class Lane(val queue: Channel<Packet>) {
        lateinit var job: Job
        @Volatile var socket: SSLSocket? = null
        fun close() {
            queue.close(); job.cancel()
            // TLS close can write close_notify. It belongs on IO even when capture stops on main.
            scope.launch(NonCancellable) { runCatching { socket?.close() } }
        }
    }

    override fun connect(link: String) {
        beforeConnect()
        scope.launch {
          lifecycle.withLock {
            disconnectNow()
            device.value = DeviceState(phase = ConnectionPhase.Connecting)
            connection = scope.launch {
                try {
                    val uri = Uri.parse(link.trim())
                    require(uri.scheme == "synqora" && uri.host == "pair") { "Paste a Synqora pairing link from your PC." }
                    val p = Pairing(uri.getQueryParameter("host") ?: "", uri.getQueryParameter("port")?.toIntOrNull() ?: 0, uri.getQueryParameter("pin") ?: "", uri.getQueryParameter("key") ?: "")
                    require(p.port in 1..65535 && p.pin.matches(Regex("[A-Fa-f0-9]{64}")) && p.key.matches(Regex("[A-Fa-f0-9]{64}"))) { "The pairing link is incomplete." }
                    address = p
                    val s = open(p); socket = s
                    val input = DataInputStream(s.inputStream); val output = DataOutputStream(s.outputStream)
                    write(output, Packet(1, json("key" to p.key, "role" to "control", "name" to "${Build.MANUFACTURER} ${Build.MODEL}", "battery" to batteryLevel())))
                    val welcome = read(input); check(welcome.kind == 2) { "PC rejected the connection." }
                    val data = JSONObject(String(welcome.payload, Charsets.UTF_8)); sessionId = data.getString("session")
                    var previousPong = SystemClock.elapsedRealtime(); var previousFrames = 0L
                    device.value = DeviceState(name = data.getString("name"), identity = "Windows PC · encrypted LAN", phase = ConnectionPhase.Connected, controlsAllowed = data.optBoolean("controlsAllowed"))
                    control = Channel(128)
                  coroutineScope {
                    val writer = launch {
                        try { for (packet in control) write(output, packet) }
                        finally { s.close() } // Wake the blocking reader on writer failure/cancellation.
                    }
                    val heartbeat = launch {
                        while (isActive) { control.send(Packet(6, json("timestamp" to SystemClock.elapsedRealtime()))); publishState(); delay(1000) }
                    }
                    try {
                        while (isActive) {
                            val frame = read(input)
                            if (frame.kind == 7) {
                                val pong = JSONObject(String(frame.payload, Charsets.UTF_8))
                                latency = (SystemClock.elapsedRealtime() - pong.getLong("timestamp")).coerceIn(0, 10000).toInt()
                                val now = SystemClock.elapsedRealtime(); val frames = pong.optLong("receivedVideoFrames")
                                val rate = ((frames - previousFrames) * 1000 / (now - previousPong).coerceAtLeast(1)).toInt().coerceAtLeast(0)
                                previousPong = now; previousFrames = frames
                                device.update { it.copy(latencyMs = latency, controlsAllowed = pong.optBoolean("controlsAllowed"), receivedAudioBytes = pong.optLong("receivedAudioBytes"), receivedVideoFrames = frames, receivedVideoFps = rate) }
                            }
                        }
                    } finally { writer.cancel(); heartbeat.cancel() }
                  }
                } catch (e: Exception) {
                    if (e !is CancellationException) device.value = DeviceState(phase = ConnectionPhase.Error, error = e.message ?: "Connection ended. Check the pairing link and local network.")
                } finally {
                    stopLanes(); socket?.close(); socket = null; sessionId = ""
                    if (device.value.phase != ConnectionPhase.Error) device.value = DeviceState()
                }
            }
          }
        }
    }
    override fun disconnect() { scope.launch { lifecycle.withLock { disconnectNow(); device.value = DeviceState() } } }
    private suspend fun disconnectNow() { socket?.close(); connection?.cancelAndJoin(); connection = null; stopLanes() }
    private fun stopLanes() = synchronized(laneGate) { audioLane?.close(); audioLane = null; videoLane?.close(); videoLane = null; microphone = false; camera = false }
    override fun microphone(active: Boolean) {
        microphone = active && device.value.phase == ConnectionPhase.Connected
        synchronized(laneGate) {
            if (microphone && audioLane == null) audioLane = lane("audio", 3).also { it.job.start() }
            if (!microphone) { audioLane?.close(); audioLane = null }
        }
        publishState()
    }
    override fun camera(active: Boolean) {
        camera = active && device.value.phase == ConnectionPhase.Connected
        synchronized(laneGate) {
            if (camera && videoLane == null) videoLane = lane("video", 1).also { it.job.start() }
            if (!camera) { videoLane?.close(); videoLane = null }
        }
        publishState()
    }
    override fun audio(bytes: ByteArray) { audioLane?.queue?.trySend(Packet(4, bytes)) }
    override fun video(bytes: ByteArray, rotation: Int) {
        if (bytes.size > 2097144) return
        val packet = ByteArray(bytes.size + 8)
        "SQJ1".toByteArray(Charsets.US_ASCII).copyInto(packet)
        packet[4] = ((rotation % 360 + 360) % 360 / 90).toByte()
        bytes.copyInto(packet, 8)
        videoLane?.queue?.trySend(Packet(9, packet))
    }
    private fun publishState() { if (device.value.phase == ConnectionPhase.Connected) control.trySend(Packet(8, json("microphone" to microphone, "camera" to camera, "latencyMs" to latency))) }
    private fun lane(role: String, capacity: Int): Lane {
        val queue = Channel<Packet>(capacity, BufferOverflow.DROP_OLDEST)
        val lane = Lane(queue)
        val pairing = address
        val activeSession = sessionId
        lane.job = scope.launch(start = CoroutineStart.LAZY) {
            var s: SSLSocket? = null
            try {
                val p = pairing ?: return@launch
                s = open(p)
                lane.socket = s
                ensureActive()
                val output = DataOutputStream(s.outputStream); val input = DataInputStream(s.inputStream)
                write(output, Packet(1, json("key" to p.key, "role" to role, "session" to activeSession)))
                check(read(input).kind == 2) { "Media connection rejected." }
                for (packet in queue) write(output, packet)
            } catch (e: Exception) { if (isActive && e !is CancellationException) device.update { it.copy(error = "$role stream stopped. Stop and restart capture to retry.") } }
            finally {
                s?.close(); queue.close()
                synchronized(laneGate) {
                    if (role == "audio" && audioLane === lane) { audioLane = null; microphone = false }
                    if (role == "video" && videoLane === lane) { videoLane = null; camera = false }
                }
                publishState()
            }
        }
        return lane
    }
    private fun command(vararg values: Pair<String, Any?>) {
        if (device.value.phase != ConnectionPhase.Connected || !device.value.controlsAllowed) return
        if (!control.trySend(Packet(3, json(*values))).isSuccess) device.update { it.copy(error = "Remote input is busy. Reconnect to retry.") }
    }
    override fun pointer(dx: Float, dy: Float) = command("type" to "move", "dx" to dx, "dy" to dy)
    override fun scroll(delta: Float) = command("type" to "scroll", "delta" to delta)
    override fun click(right: Boolean) = command("type" to "click", "right" to right)
    override fun text(value: String) = command("type" to "text", "text" to value.take(4096))
    override fun key(value: String, modifiers: Int) = command("type" to "key", "key" to value, "modifiers" to modifiers)
    override fun edit(move: Int, before: Int, after: Int, text: String) {
        // Bound IME replacements to the receiver's limits. Normal keystrokes are
        // single tiny edits; long pastes remain ordered and do not block the UI.
        var remainingMove = move
        while (remainingMove != 0) { val step = remainingMove.coerceIn(-256, 256); command("type" to "edit", "move" to step, "before" to 0, "after" to 0, "text" to ""); remainingMove -= step }
        var left = before; var right = after
        while (left > 0 || right > 0) { val a = left.coerceAtMost(256); val b = right.coerceAtMost(256); command("type" to "edit", "move" to 0, "before" to a, "after" to b, "text" to ""); left -= a; right -= b }
        var at = 0
        while (at < text.length) { var end = (at + 1024).coerceAtMost(text.length); if (end < text.length && text[end].isLowSurrogate()) end--; command("type" to "edit", "move" to 0, "before" to 0, "after" to 0, "text" to text.substring(at, end)); at = end }
    }
    override fun media(command: String) = command("type" to "media", "command" to command)
    override fun volume(level: Float) = command("type" to "volume", "level" to level)
    fun close() { scope.launch { lifecycle.withLock { disconnectNow() }; scope.cancel() } }

    private fun open(p: Pairing): SSLSocket {
        val resolved = InetAddress.getByName(p.host)
        require(resolved.isSiteLocalAddress || resolved.isLoopbackAddress || resolved.isLinkLocalAddress) { "Use a PC address on your local network." }
        val expected = p.pin.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        val trust = object : X509TrustManager {
            override fun getAcceptedIssuers() = emptyArray<X509Certificate>()
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = throw java.security.cert.CertificateException("Client certificates unsupported")
            override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                val leaf = chain?.firstOrNull() ?: throw java.security.cert.CertificateException("Missing PC certificate")
                leaf.checkValidity()
                if (!MessageDigest.isEqual(expected, MessageDigest.getInstance("SHA-256").digest(leaf.encoded))) throw java.security.cert.CertificateException("PC identity does not match the pairing link.")
            }
        }
        val tls = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), SecureRandom()) }
        val socket = tls.socketFactory.createSocket() as SSLSocket
        socket.enabledProtocols = socket.supportedProtocols.filter { it == "TLSv1.3" || it == "TLSv1.2" }.toTypedArray()
        try {
            socket.soTimeout = 15000; socket.tcpNoDelay = true; socket.connect(InetSocketAddress(resolved, p.port), 5000); socket.startHandshake(); return socket
        } catch (e: Exception) { socket.close(); throw e }
    }
    private fun read(input: DataInputStream): Packet {
        val kind = input.readUnsignedByte(); val length = input.readInt()
        require(kind in 1..8 && length in 0..16384) { "Invalid PC control frame." }
        return Packet(kind, ByteArray(length).also { input.readFully(it) })
    }
    private fun write(output: DataOutputStream, packet: Packet) {
        val frame = java.nio.ByteBuffer.allocate(packet.payload.size + 5).put(packet.kind.toByte()).putInt(packet.payload.size).put(packet.payload).array()
        output.write(frame); output.flush()
    }
    private fun json(vararg pairs: Pair<String, Any?>) = JSONObject().apply { pairs.forEach { put(it.first, it.second ?: JSONObject.NULL) } }.toString().toByteArray(Charsets.UTF_8)
}
