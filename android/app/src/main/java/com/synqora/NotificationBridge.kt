package com.synqora

import android.app.*
import android.content.*
import android.net.Uri
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import com.synqora.data.ConnectionPhase
import com.synqora.data.LanSession
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/** Opt-in, event-driven notification bridge. PendingIntents never cross the network. */
class NotificationBridge : NotificationListenerService() {
    private val state get() = (application as SynqoraApplication).state
    private val session get() = state.session as LanSession
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val refreshes = Channel<Unit>(Channel.CONFLATED)
    private data class Target(val key: String, val postTime: Long, val version: String, val actions: Map<String, Notification.Action>)
    private val targets = mutableMapOf<String, Target>()
    private val ids = mutableMapOf<String, String>()
    private val sent = mutableMapOf<String, String>()
    override fun onCreate() {
        super.onCreate(); current = this
        scope.launch { for (ignored in refreshes) { delay(300); runCatching { snapshot() } } }
    }
    override fun onListenerConnected() { refresh() }
    override fun onNotificationPosted(sbn: StatusBarNotification?) { refresh() }
    override fun onNotificationRemoved(sbn: StatusBarNotification?) { refresh() }
    override fun onListenerDisconnected() { targets.clear(); ids.clear(); sent.clear(); session.companion(JSONObject().put("type", "notificationsClear")) }
    override fun onDestroy() { if (current === this) current = null; scope.cancel(); targets.clear(); sent.clear(); super.onDestroy() }
    fun refresh() { refreshes.trySend(Unit) }
    fun reset() { scope.launch { targets.clear(); ids.clear(); sent.clear() } }
    private fun enabled() = state.companion.syncEnabled.value && state.session.device.value.phase == ConnectionPhase.Connected
    private fun snapshot() {
        if (!enabled()) { targets.clear(); ids.clear(); sent.clear(); return }
        val notifications = runCatching { activeNotifications.toList() }.getOrDefault(emptyList()).filter {
            it.packageName != packageName && it.notification.visibility != Notification.VISIBILITY_SECRET && it.notification.flags and Notification.FLAG_GROUP_SUMMARY == 0
        }.sortedByDescending { it.postTime }.take(80)
        val live = notifications.map { it.key }.toSet()
        ids.keys.filter { it !in live }.toList().forEach { key ->
            val id = ids.remove(key)!!; sent.remove(id); targets.remove(id)
            session.companion(JSONObject().put("type", "notificationRemove").put("id", id))
        }
        notifications.forEach { sbn ->
            val n = sbn.notification; val id = ids.getOrPut(sbn.key) { UUID.randomUUID().toString() }
            val title = n.extras.getCharSequence(Notification.EXTRA_TITLE)?.toString().orEmpty().take(200)
            val text = (n.extras.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: n.extras.getCharSequence(Notification.EXTRA_TEXT))?.toString().orEmpty().take(3000)
            val candidates = n.actions.orEmpty().filter { a -> a.remoteInputs?.any { it.allowFreeFormInput } == true || n.category == Notification.CATEGORY_CALL && a.remoteInputs.isNullOrEmpty() }.take(4)
            // A version changes whenever content or available actions change; stale buttons cannot reply to a newer notification.
            val signature = "${sbn.postTime}|$title|$text|${candidates.joinToString { it.title.toString() + it.actionIntent.hashCode() }}"
            if (sent[id] == signature) return@forEach
            val version = UUID.randomUUID().toString(); val actions = candidates.associateBy { UUID.randomUUID().toString() }
            val appName = runCatching { packageManager.getApplicationLabel(packageManager.getApplicationInfo(sbn.packageName, 0)).toString() }.getOrDefault(sbn.packageName)
            val event = JSONObject().put("type", "notification").put("id", id).put("version", version).put("app", appName.take(100)).put("title", title).put("text", text).put("time", sbn.postTime).put("call", n.category == Notification.CATEGORY_CALL)
            val actionList = JSONArray(); actions.forEach { (actionId, a) -> actionList.put(JSONObject().put("id", actionId).put("label", a.title.toString().take(80)).put("reply", a.remoteInputs?.any { it.allowFreeFormInput } == true)) }; event.put("actions", actionList)
            if (session.companion(event)) { targets[id] = Target(sbn.key, sbn.postTime, version, actions); sent[id] = signature }
        }
        session.companion(JSONObject().put("type", "notificationStatus").put("enabled", true).put("count", notifications.size))
    }
    fun command(event: JSONObject) { scope.launch {
        if (!enabled()) { result(event.optString("request"), false, "Notification sync is paused on your phone."); return@launch }
        val request = event.optString("request")
        try {
            when (event.optString("type")) {
                "notificationRefresh" -> { sent.clear(); refresh(); return@launch }
                "dial" -> { promptDial(event.getString("number")); result(request, true, "Tap the dial request on your phone."); return@launch }
            }
            val target = targets[event.getString("id")] ?: error("Notification is no longer available.")
            check(target.version == event.getString("version")) { "Notification changed. Try its latest action." }
            val active = activeNotifications.firstOrNull { it.key == target.key && it.postTime == target.postTime } ?: error("Notification has expired.")
            when (event.getString("type")) {
                "notificationDismiss" -> cancelNotification(target.key)
                "notificationAction" -> {
                    val a = target.actions[event.getString("action")] ?: error("Action expired.")
                    check(active.notification.actions.orEmpty().any { it.actionIntent == a.actionIntent })
                    if (android.os.Build.VERSION.SDK_INT >= 31 && a.isAuthenticationRequired) check(!getSystemService(KeyguardManager::class.java).isDeviceLocked) { "Unlock the phone to use this action." }
                    val inputs = a.remoteInputs?.filter { it.allowFreeFormInput }.orEmpty()
                    val fill = Intent()
                    if (inputs.isNotEmpty()) {
                        val reply = event.getString("text"); require(reply.isNotBlank() && reply.length <= 4096)
                        val values = Bundle().apply { inputs.forEach { putCharSequence(it.resultKey, reply) } }
                        RemoteInput.addResultsToIntent(inputs.toTypedArray(), fill, values)
                        RemoteInput.setResultsSource(fill, RemoteInput.SOURCE_FREE_FORM_INPUT)
                    } else check(active.notification.category == Notification.CATEGORY_CALL)
                    a.actionIntent.send(this@NotificationBridge, 0, fill)
                }
                else -> return@launch
            }
            result(request, true, "Sent to the phone app."); refresh()
        } catch (_: Exception) { result(request, false, "This action is unavailable or expired. Check your phone.") }
    } }
    private fun result(request: String, ok: Boolean, message: String) { session.companion(JSONObject().put("type", "actionResult").put("request", request.take(64)).put("ok", ok).put("message", message)) }
    private fun promptDial(number: String) {
        require(number.length in 1..40 && number.all { it in "0123456789+*# ()-" })
        check(getSystemService(NotificationManager::class.java).areNotificationsEnabled())
        val intent = Intent(Intent.ACTION_DIAL, Uri.fromParts("tel", number, null))
        val pending = PendingIntent.getActivity(this, 44, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel("dial-requests", "Phone dial requests", NotificationManager.IMPORTANCE_DEFAULT))
        manager.notify(84, Notification.Builder(this, "dial-requests").setSmallIcon(R.drawable.synqora_mark).setContentTitle("Dial request from your PC").setContentText("Tap to open $number in your phone dialer").setContentIntent(pending).setAutoCancel(true).build())
    }
    companion object {
        @Volatile var current: NotificationBridge? = null; private set
    }
}
