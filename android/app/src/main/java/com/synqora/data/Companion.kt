package com.synqora.data

import android.app.Application
import android.content.ContentValues
import android.net.Uri
import android.os.PowerManager
import android.provider.OpenableColumns
import com.synqora.NotificationBridge
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject
import java.io.*
import java.security.MessageDigest
import java.util.UUID
import javax.net.ssl.SSLSocket

data class FileTransfer(val id: String, val name: String, val size: Long, val incoming: Boolean, val status: String = "Waiting", val done: Long = 0)
class Companion(private val app: Application, private val session: LanSession, private val scope: CoroutineScope) {
    private data class Entry(val view: FileTransfer, val hash: String, val source: Uri? = null)
    private val gate = Any()
    private val entries = linkedMapOf<String, Entry>()
    private val serial = Mutex()
    private val preparation = Mutex()
    private val preparations = mutableSetOf<Job>()
    private val sockets = mutableMapOf<String, SSLSocket>()
    private val jobs = mutableMapOf<String, Job>()
    val transfers = MutableStateFlow<List<FileTransfer>>(emptyList())
    val message = MutableStateFlow<String?>(null)
    private val prefs = app.getSharedPreferences("synqora", 0)
    val syncEnabled = MutableStateFlow(prefs.getBoolean("notification_sync", false))
    @Volatile var onGallery: (JSONObject) -> Unit = {}
    @Volatile var onCalls: (JSONObject) -> Unit = {}
    init {
        session.onCompanion = { event ->
            try { when (event.getString("type")) {
                "fileOffer" -> {
                    val id = event.getString("id"); val name = safeName(event.getString("name")); val size = event.getLong("size"); val hash = event.getString("hash")
                    require(id.matches(Regex("[a-fA-F0-9]{32}")) && size >= 0 && hash.matches(Regex("[a-fA-F0-9]{64}")))
                    synchronized(gate) { if (!entries.containsKey(id)) { room(); entries[id] = Entry(FileTransfer(id, name, size, true), hash); publish() } }
                    incomingAlert(name)
                }
                "fileAccept" -> {
                    val id = event.getString("id")
                    val e = synchronized(gate) { entries[id]?.takeIf { !it.view.incoming && it.view.status == "Waiting" } }
                    if (e != null) transfer(id) { upload(e) }
                }
                "fileCancel" -> cancel(event.getString("id"), false)
                else -> when {
                    event.optString("type").startsWith("gallery") -> onGallery(event)
                    event.optString("type") in listOf("callsHistory", "callPlace") -> onCalls(event)
                    else -> NotificationBridge.current?.command(event)
                }
            } } catch (_: Exception) { message.value = "The companion request could not be completed." }
        }
        scope.launch {
            session.device.map { it.phase }.distinctUntilChanged().collect { phase ->
                if (phase == ConnectionPhase.Connected) NotificationBridge.current?.refresh()
                else {
                    NotificationBridge.current?.reset()
                    synchronized(gate) {
                        preparations.toList().forEach { it.cancel() }
                        jobs.values.toList().forEach { it.cancel() }; sockets.values.toList().forEach { socket -> scope.launch(Dispatchers.IO) { runCatching { socket.close() } } }
                        entries.replaceAll { _, e -> if (e.view.status in listOf("Waiting", "Ready", "Transferring")) e.copy(view = e.view.copy(status = "Disconnected")) else e }; publish()
                    }
                }
            }
        }
    }
    private fun incomingAlert(name: String) {
        val manager = app.getSystemService(android.app.NotificationManager::class.java)
        if (!manager.areNotificationsEnabled()) return
        manager.createNotificationChannel(android.app.NotificationChannel("file-requests", "Incoming files", android.app.NotificationManager.IMPORTANCE_DEFAULT))
        val open = android.app.PendingIntent.getActivity(app, 45, android.content.Intent(app, com.synqora.MainActivity::class.java).addFlags(android.content.Intent.FLAG_ACTIVITY_CLEAR_TOP or android.content.Intent.FLAG_ACTIVITY_SINGLE_TOP).putExtra("destination", "Files"), android.app.PendingIntent.FLAG_UPDATE_CURRENT or android.app.PendingIntent.FLAG_IMMUTABLE)
        manager.notify(85, android.app.Notification.Builder(app, "file-requests").setSmallIcon(com.synqora.R.drawable.synqora_mark).setContentTitle("File from your computer").setContentText(name).setContentIntent(open).setAutoCancel(true).build())
    }
    fun sync(value: Boolean) {
        syncEnabled.value = value; prefs.edit().putBoolean("notification_sync", value).apply()
        NotificationBridge.current?.refresh()
        if (!value) session.companion(JSONObject().put("type", "notificationsClear"))
    }
    fun offer(uri: Uri, request: String? = null) {
      val work = scope.launch(Dispatchers.IO, start = CoroutineStart.LAZY) { preparation.withLock {
        try {
            check(session.device.value.phase == ConnectionPhase.Connected) { "Connect your computer first." }
            val name = app.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { if (it.moveToFirst()) it.getString(0) else null } ?: "Shared file"
            val digest = MessageDigest.getInstance("SHA-256"); var size = 0L; val buffer = ByteArray(65536)
            app.contentResolver.openInputStream(uri)!!.use { input -> var n: Int; while (input.read(buffer).also { n = it } > 0) { ensureActive(); size = Math.addExact(size, n.toLong()); digest.update(buffer, 0, n) } }
            val id = UUID.randomUUID().toString().replace("-", ""); val hash = digest.digest().hex()
            synchronized(gate) { room(); entries[id] = Entry(FileTransfer(id, safeName(name), size, false), hash, uri); publish() }
            check(session.companion(JSONObject().put("type", "fileOffer").put("id", id).put("name", name).put("size", size).put("hash", hash).put("request", request))) { "The computer disconnected." }
        } catch (e: Exception) { if (e !is CancellationException) message.value = e.message ?: "Could not share this file." }
      } }
      synchronized(gate) { preparations.add(work) }
      work.invokeOnCompletion { synchronized(gate) { preparations.remove(work) } }
      work.start()
    }
    fun accept(id: String, destination: Uri) {
        val e = synchronized(gate) { entries[id]?.takeIf { it.view.incoming && it.view.status == "Waiting" } } ?: return
        transfer(id) { download(e, destination) }
    }
    fun cancel(id: String, inform: Boolean = true) {
        synchronized(gate) { jobs[id]?.cancel(); sockets[id]?.let { socket -> scope.launch(Dispatchers.IO) { runCatching { socket.close() } } }; update(id) { it.copy(status = "Cancelled") } }
        if (inform) session.companion(JSONObject().put("type", "fileCancel").put("id", id))
    }
    private fun transfer(id: String, block: suspend () -> Unit) {
        synchronized(gate) {
            if (jobs[id]?.isActive == true) return
            update(id) { it.copy(status = "Ready") }
            jobs[id] = scope.launch(Dispatchers.IO) {
                try { serial.withLock {
                    val wake = app.getSystemService(PowerManager::class.java).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Synqora:FileTransfer")
                    wake.acquire(10 * 60_000L)
                    try { update(id) { it.copy(status = "Transferring") }; block(); update(id) { it.copy(status = "Complete") } }
                    finally { if (wake.isHeld) wake.release() }
                } } catch (e: Exception) { if (e !is CancellationException) { update(id) { it.copy(status = "Failed") }; message.value = e.message ?: "Transfer failed. Reconnect and try again."; session.companion(JSONObject().put("type", "fileCancel").put("id", id)) } }
                finally { synchronized(gate) { sockets.remove(id)?.let { runCatching { it.close() } }; jobs.remove(id) } }
            }
        }
    }
    private suspend fun upload(e: Entry) {
        val socket = session.openFile(e.view.id, true); synchronized(gate) { sockets[e.view.id] = socket }
        val output = DataOutputStream(socket.outputStream); val input = DataInputStream(socket.inputStream)
        app.contentResolver.openInputStream(e.source!!)!!.use { source ->
            val buffer = ByteArray(65536); var done = 0L; var count: Int
            while (source.read(buffer).also { count = it } > 0) { currentCoroutineContext().ensureActive(); done += count; check(done <= e.view.size); write(output, 11, buffer, count); progress(e.view.id, done) }
            check(done == e.view.size); write(output, 12, ByteArray(0), 0)
            check(read(input).first == 12) { "Delivery was not confirmed." }
        }
    }
    private suspend fun download(e: Entry, destination: Uri) {
        check(app.cacheDir.usableSpace - 16 * 1024 * 1024 >= e.view.size) { "Not enough free space on this phone to receive the file." }
        val partial = File.createTempFile("synqora-", ".part", app.cacheDir)
        try {
            val socket = session.openFile(e.view.id, false); synchronized(gate) { sockets[e.view.id] = socket }
            val input = DataInputStream(socket.inputStream); var done = 0L; val hash = MessageDigest.getInstance("SHA-256")
            partial.outputStream().use { output -> while (true) {
                currentCoroutineContext().ensureActive(); val (kind, bytes) = read(input)
                if (kind == 12) break
                check(kind == 11 && bytes.isNotEmpty() && bytes.size <= e.view.size - done)
                output.write(bytes); hash.update(bytes); done += bytes.size; progress(e.view.id, done)
            } }
            check(done == e.view.size && hash.digest().hex().equals(e.hash, true)) { "File integrity check failed." }
            currentCoroutineContext().ensureActive()
            app.contentResolver.openOutputStream(destination, "wt")!!.use { output -> partial.inputStream().use { it.copyTo(output, 65536) } }
            write(DataOutputStream(socket.outputStream), 12, ByteArray(0), 0)
        } catch (e: Exception) { runCatching { android.provider.DocumentsContract.deleteDocument(app.contentResolver, destination) }; throw e }
        finally { partial.delete() }
    }
    private fun update(id: String, change: (FileTransfer) -> FileTransfer) = synchronized(gate) { entries[id]?.let { entries[id] = it.copy(view = change(it.view)); publish() }; Unit }
    private fun progress(id: String, done: Long) = synchronized(gate) { entries[id]?.let { val notify = done == it.view.size || done / 262144 != it.view.done / 262144; entries[id] = it.copy(view = it.view.copy(done = done)); if (notify) publish() }; Unit }
    private fun publish() { transfers.value = entries.values.map { it.view }.reversed() }
    private fun room() { if (entries.size >= 50) { val old = entries.entries.firstOrNull { it.value.view.status !in listOf("Waiting", "Ready", "Transferring") }; check(old != null) { "Finish pending transfers first." }; entries.remove(old.key) } }
    companion object {
        fun safeName(name: String): String { require(name.length in 1..180 && name != "." && name != ".." && name.none { it.isISOControl() || it in "<>:\"/\\|?*" } && !name.endsWith('.') && !name.endsWith(' ')) { "Unsupported filename." }; require(!Regex("(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\..*)?").matches(name)); return name }
        private fun ByteArray.hex() = joinToString("") { "%02x".format(it) }
        private fun read(input: DataInputStream): Pair<Int, ByteArray> { val kind = input.readUnsignedByte(); val size = input.readInt(); require(kind in 11..12 && size in 0..65536 && (kind != 12 || size == 0)); return kind to ByteArray(size).also { input.readFully(it) } }
        private fun write(output: DataOutputStream, kind: Int, bytes: ByteArray, count: Int) { output.writeByte(kind); output.writeInt(count); output.write(bytes, 0, count); output.flush() }
    }
}
