package com.synqora

import android.app.*
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.PowerManager
import android.os.Build
import androidx.core.content.ContextCompat
import androidx.core.app.ServiceCompat
import androidx.lifecycle.LifecycleService
import androidx.lifecycle.lifecycleScope
import com.synqora.data.ConnectionPhase
import com.synqora.data.SessionStatus
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.collectLatest

/** Explicit user-started LAN session. Stop remains available from its notification. */
class SessionService : LifecycleService() {
    private val state get() = (application as SynqoraApplication).state
    private lateinit var wake: PowerManager.WakeLock
    private var status = SessionStatus()
    override fun onCreate() {
        super.onCreate()
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "Active PC connection", NotificationManager.IMPORTANCE_LOW))
        wake = getSystemService(PowerManager::class.java).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Synqora:Session").apply { setReferenceCounted(false) }
        showNotification(connectedType())
        state.camera.attach(this)
        lifecycleScope.launch {
            state.status.collectLatest { current ->
                status = current
                if (!current.keepAlive) {
                    // The connect request and service startup can arrive in either order.
                    delay(5000)
                    if (!state.status.value.keepAlive) stopSelf()
                } else {
                    showNotification(types(current.microphone, current.camera))
                    wake.acquire(10 * 60_000L)
                }
            }
        }
        lifecycleScope.launch {
            while (isActive) {
                delay(5 * 60_000L)
                if (status.keepAlive) wake.acquire(10 * 60_000L)
            }
        }
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        super.onStartCommand(intent, flags, startId)
        if (intent?.action == STOP) {
            state.stopMicrophone(); state.camera.stop(); state.session.disconnect(); stopSelf()
            return START_NOT_STICKY
        }
        // Promote the required while-in-use type before opening capture hardware.
        showNotification(types(intent?.getBooleanExtra("microphone", false) == true || state.microphoneRunning, intent?.getBooleanExtra("camera", false) == true || state.camera.state.value.active))
        return START_NOT_STICKY
    }
    private fun showNotification(type: Int) {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, SessionService::class.java).setAction(STOP), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val activities = listOfNotNull(if (state.microphoneRunning) "microphone" else null, if (state.camera.state.value.active) "camera" else null)
        val message = if (activities.isNotEmpty()) "Active: ${activities.joinToString(" + ")}. Continues with the screen off." else "Your PC connection stays active with the screen off."
        val notification = Notification.Builder(this, CHANNEL).setSmallIcon(R.drawable.synqora_mark).setContentTitle(if (state.status.value.phase == ConnectionPhase.Connected) "Connected to ${state.status.value.name}" else "Synqora session").setContentText(message).setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_SERVICE).addAction(Notification.Action.Builder(null, "Disconnect", stop).build()).build()
        ServiceCompat.startForeground(this, ID, notification, type)
    }
    override fun onDestroy() {
        state.stopMicrophone(); state.camera.stop(); state.camera.detach(this)
        if (state.status.value.phase == ConnectionPhase.Connected || state.status.value.phase == ConnectionPhase.Connecting) state.session.disconnect()
        if (wake.isHeld) wake.release()
        super.onDestroy()
    }
    companion object {
        private const val CHANNEL = "synqora-session"
        private const val ID = 83
        private const val STOP = "com.synqora.STOP_SESSION"
        private fun connectedType() = ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
        private fun types(mic: Boolean, camera: Boolean) = connectedType() or (if (mic && Build.VERSION.SDK_INT >= 30) ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE else 0) or (if (camera && Build.VERSION.SDK_INT >= 30) ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA else 0)
        fun ensure(context: Context, microphone: Boolean = false, camera: Boolean = false) {
            ContextCompat.startForegroundService(context, Intent(context, SessionService::class.java).putExtra("microphone", microphone).putExtra("camera", camera))
        }
    }
}
