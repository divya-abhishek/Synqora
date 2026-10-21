package com.synqora

import android.Manifest
import android.app.*
import android.content.*
import android.net.Uri
import android.os.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import com.synqora.data.*
import org.json.JSONObject
import org.junit.*
import java.io.File
import java.security.MessageDigest
import android.provider.MediaStore
import android.content.ContentValues
import android.graphics.Bitmap
import android.provider.CallLog

class CompanionIntegrationTest {
    @get:Rule val app = createAndroidComposeRule<MainActivity>()
    private fun await(timeout: Long = 20000, condition: () -> Boolean) {
        val end = SystemClock.elapsedRealtime() + timeout
        while (!condition() && SystemClock.elapsedRealtime() < end) Thread.sleep(100)
        check(condition()) { "Companion integration condition timed out." }
    }
    @Test fun files_reply_and_capture_teardown() {
        val instrumentation = InstrumentationRegistry.getInstrumentation(); val ui = instrumentation.uiAutomation
        fun shell(command: String) = ParcelFileDescriptor.AutoCloseInputStream(ui.executeShellCommand(command)).bufferedReader().use { it.readText() }
        check(shell("getprop ro.kernel.qemu").trim() == "1") { "This test is restricted to a disposable emulator." }
        val context = instrumentation.targetContext; val testContext = instrumentation.context
        val invite = context.getFileStreamPath("test-pairing.txt").readText()
        val model = (app.activity.application as SynqoraApplication).state
        ui.grantRuntimePermission(context.packageName, Manifest.permission.RECORD_AUDIO)
        ui.grantRuntimePermission(context.packageName, Manifest.permission.CAMERA)
        ui.grantRuntimePermission(testContext.packageName, Manifest.permission.POST_NOTIFICATIONS)
        shell("cmd notification allow_listener com.synqora/.NotificationBridge")
        var fixturePhoto: Uri? = null
        var fixtureCall: Uri? = null
        try {
            app.runOnUiThread { model.session.connect(invite) }
            await { model.session.device.value.phase == ConnectionPhase.Connected }
            app.runOnUiThread { model.companion.sync(true) }
            await { NotificationBridge.current != null }
            shell("am start -W -n com.synqora.test/com.synqora.FixtureActivity")
            await { shell("run-as com.synqora.test cat files/fixture-reply.txt").trim() == "Synqora integration reply" }
            await { model.companion.transfers.value.any { it.incoming && it.status == "Waiting" } }
            val incoming = model.companion.transfers.value.first { it.incoming }
            val received = File(context.cacheDir, "received.bin"); received.delete()
            app.runOnUiThread { model.companion.accept(incoming.id, Uri.fromFile(received)) }
            await(30000) { model.companion.transfers.value.any { it.id == incoming.id && it.status == "Complete" } }
            check(received.length() == 1_048_737L)
            app.runOnUiThread { model.companion.offer(Uri.fromFile(received)) }
            await(30000) { model.companion.transfers.value.any { !it.incoming && it.status == "Complete" } }
            ui.grantRuntimePermission(context.packageName, Manifest.permission.READ_MEDIA_IMAGES)
            ui.grantRuntimePermission(context.packageName, Manifest.permission.READ_MEDIA_VIDEO)
            fixturePhoto = context.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, ContentValues().apply { put(MediaStore.Images.Media.DISPLAY_NAME, "synqora-gallery-fixture.png"); put(MediaStore.Images.Media.MIME_TYPE, "image/png"); put(MediaStore.Images.Media.IS_PENDING, 1) })!!
            val bitmap = Bitmap.createBitmap(64, 64, Bitmap.Config.ARGB_8888); bitmap.eraseColor(android.graphics.Color.DKGRAY)
            context.contentResolver.openOutputStream(fixturePhoto!!)!!.use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }; bitmap.recycle()
            context.contentResolver.update(fixturePhoto!!, ContentValues().apply { put(MediaStore.Images.Media.IS_PENDING, 0) }, null, null)
            app.runOnUiThread { model.gallery.sharing(true) }
            await(30000) { model.companion.transfers.value.any { !it.incoming && it.name == "synqora-gallery-fixture.png" && it.status == "Complete" } }
            ui.grantRuntimePermission(context.packageName, Manifest.permission.READ_CALL_LOG)
            ui.adoptShellPermissionIdentity(Manifest.permission.WRITE_CALL_LOG)
            try { fixtureCall = context.contentResolver.insert(CallLog.Calls.CONTENT_URI, ContentValues().apply { put(CallLog.Calls.NUMBER, "5550100"); put(CallLog.Calls.TYPE, CallLog.Calls.OUTGOING_TYPE); put(CallLog.Calls.DATE, System.currentTimeMillis()); put(CallLog.Calls.DURATION, 12) }) }
            finally { ui.dropShellPermissionIdentity() }
            app.runOnUiThread { model.calls.history(true) }
            Thread.sleep(1000)
            check(shell("dumpsys power").lineSequence().none { it.contains("Synqora:Session") && it.contains("WAKE_LOCK") }) { "Idle companion must not hold a capture wake lock." }
            val cpuStart = android.os.Process.getElapsedCpuTime()
            // A large idle gap must not make the first capture heartbeat time out.
            Thread.sleep(13000)
            android.util.Log.i("SynqoraCompanionTest", "Idle connected CPU: ${android.os.Process.getElapsedCpuTime() - cpuStart} ms over 13000 ms; no capture wake lock")
            app.runOnUiThread { model.startMicrophone(); model.startCamera() }
            await(30000) { model.session.device.value.receivedVideoFrames > 3 && model.session.device.value.receivedAudioBytes > 0 }
            check(shell("dumpsys power").lineSequence().any { it.contains("Synqora:Session") && it.contains("WAKE_LOCK") }) { "Active capture wake lock missing." }
            (model.session as LanSession).companion(JSONObject().put("type", "fixtureDisconnect"))
            await { model.session.device.value.phase != ConnectionPhase.Connected && !model.microphoneRunning && !model.camera.state.value.active }
            Thread.sleep(1000)
            val power = shell("dumpsys power")
            check(power.lineSequence().none { it.contains("Synqora:Session") && it.contains("WAKE_LOCK") }) { "Capture wake lock leaked after PC disconnect." }
            val cameras = shell("dumpsys media.camera")
            check(Regex("Active Camera Clients:\\s*\\[\\s*]").containsMatchIn(cameras)) { "Camera hardware remained open after disconnect." }
            android.util.Log.i("SynqoraCompanionTest", "PASS: bidirectional files, notification reply, gallery original, call history, PC disconnect releases camera, microphone and wake lock")
        } finally {
            shell("am start -W -a fixture.clean -n com.synqora.test/com.synqora.FixtureActivity")
            app.runOnUiThread { model.companion.sync(false); model.stopCapture(); model.session.disconnect() }
            app.runOnUiThread { model.gallery.sharing(false); model.calls.history(false) }
            fixturePhoto?.let { context.contentResolver.delete(it, null, null) }
            ui.adoptShellPermissionIdentity(Manifest.permission.WRITE_CALL_LOG)
            try { fixtureCall?.let { context.contentResolver.delete(CallLog.Calls.CONTENT_URI, "${CallLog.Calls._ID} = ?", arrayOf(android.content.ContentUris.parseId(it).toString())) } } finally { ui.dropShellPermissionIdentity() }
            shell("cmd notification disallow_listener com.synqora/.NotificationBridge")
        }
    }
}
