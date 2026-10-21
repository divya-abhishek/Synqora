package com.synqora.data

import android.Manifest
import android.app.Application
import android.content.pm.PackageManager
import android.database.ContentObserver
import android.net.Uri
import android.os.*
import android.provider.CallLog
import android.telecom.TelecomManager
import android.telephony.PhoneNumberUtils
import androidx.core.content.ContextCompat
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.*
import org.json.JSONArray
import org.json.JSONObject

/** Explicitly enabled call history and dial requests. Call audio belongs to the OS route. */
class CallBridge(private val app: Application, private val session: LanSession, companion: Companion, private val scope: CoroutineScope) {
    private val prefs = app.getSharedPreferences("synqora", 0)
    val history = MutableStateFlow(prefs.getBoolean("call_history", false))
    val dialing = MutableStateFlow(prefs.getBoolean("call_dialing", false))
    val access = MutableStateFlow("Call sharing is off")
    private val commands = Channel<JSONObject>(8)
    private var observing = false
    private var changes: Job? = null
    private var lastDial = 0L
    private val handled = ArrayDeque<String>(32)
    private val observer = object : ContentObserver(Handler(Looper.getMainLooper())) {
        override fun onChange(selfChange: Boolean) { changes?.cancel(); changes = scope.launch { delay(500); send(JSONObject().put("type", "callsChanged")) } }
    }
    init {
        companion.onCalls = { commands.trySend(it) }
        scope.launch { combine(session.device.map { it.phase }.distinctUntilChanged(), history, dialing) { _, _, _ -> Unit }.collect { refreshAccess() } }
        scope.launch(Dispatchers.IO) { for (event in commands) {
            if (session.device.value.phase != ConnectionPhase.Connected) continue
            try { when (event.optString("type")) {
                "callsHistory" -> readHistory()
                "callPlace" -> placeCall(event)
            } } catch (_: Exception) { send(JSONObject().put("type", "actionResult").put("request", event.optString("request")).put("ok", false).put("message", "Call access is unavailable. Check permissions and calling settings on your phone.")) }
        } }
    }
    fun history(value: Boolean) { history.value = value; prefs.edit().putBoolean("call_history", value).apply(); refreshAccess() }
    fun dialing(value: Boolean) { dialing.value = value; prefs.edit().putBoolean("call_dialing", value).apply(); refreshAccess() }
    fun granted(permission: String) = ContextCompat.checkSelfPermission(app, permission) == PackageManager.PERMISSION_GRANTED
    fun refreshAccess() {
        val read = history.value && granted(Manifest.permission.READ_CALL_LOG)
        val dial = dialing.value && granted(Manifest.permission.CALL_PHONE)
        access.value = if (read || dial) listOfNotNull(if (read) "Call history shared" else null, if (dial) "PC dialing allowed" else null).joinToString(" · ") else "Choose call permissions on this phone"
        if (read && session.device.value.phase == ConnectionPhase.Connected && !observing) { app.contentResolver.registerContentObserver(CallLog.Calls.CONTENT_URI, true, observer); observing = true }
        else if ((!read || session.device.value.phase != ConnectionPhase.Connected) && observing) { app.contentResolver.unregisterContentObserver(observer); observing = false }
        send(JSONObject().put("type", "callsStatus").put("history", read).put("dial", dial).put("message", access.value))
    }
    private fun readHistory() {
        if (!history.value || !granted(Manifest.permission.READ_CALL_LOG)) { refreshAccess(); return }
        val items = JSONArray()
        val projection = arrayOf(CallLog.Calls.NUMBER, CallLog.Calls.CACHED_NAME, CallLog.Calls.DATE, CallLog.Calls.DURATION, CallLog.Calls.TYPE)
        val uri = CallLog.Calls.CONTENT_URI.buildUpon().appendQueryParameter("limit", "50").build()
        app.contentResolver.query(uri, projection, null, null, "${CallLog.Calls.DATE} DESC")?.use { cursor -> while (items.length() < 50 && cursor.moveToNext()) {
            items.put(JSONObject().put("number", cursor.getString(0).orEmpty().take(80)).put("name", cursor.getString(1).orEmpty().take(160)).put("time", cursor.getLong(2)).put("duration", cursor.getLong(3)).put("kind", cursor.getInt(4)))
        } }
        if (history.value && granted(Manifest.permission.READ_CALL_LOG)) send(JSONObject().put("type", "callsHistory").put("items", items))
    }
    @Suppress("DEPRECATION")
    private fun placeCall(event: JSONObject) {
        check(dialing.value && granted(Manifest.permission.CALL_PHONE))
        val request = event.getString("request"); val number = event.getString("number").trim()
        require(request.matches(Regex("[a-fA-F0-9]{32}")) && request !in handled)
        require(number.length in 1..40 && number.all { it in "+0123456789 ()-" } && number.any { it.isDigit() })
        check(!PhoneNumberUtils.isEmergencyNumber(number))
        check(SystemClock.elapsedRealtime() - lastDial > 2000)
        lastDial = SystemClock.elapsedRealtime(); if (handled.size == 32) handled.removeFirst(); handled.addLast(request)
        check(app.packageManager.hasSystemFeature(PackageManager.FEATURE_TELEPHONY))
        if (ContextCompat.checkSelfPermission(app, Manifest.permission.CALL_PHONE) != PackageManager.PERMISSION_GRANTED) throw SecurityException("Calling permission was revoked.")
        try { app.getSystemService(TelecomManager::class.java).placeCall(Uri.fromParts("tel", number, null), Bundle()) }
        catch (error: SecurityException) { throw IllegalStateException("Android did not allow the call.", error) }
        send(JSONObject().put("type", "actionResult").put("request", request).put("ok", true).put("message", "Call requested. Audio uses your phone's configured call route."))
    }
    private fun send(event: JSONObject) = session.companion(event)
}
