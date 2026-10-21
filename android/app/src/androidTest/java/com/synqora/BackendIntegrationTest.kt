package com.synqora

import android.Manifest
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test

class BackendIntegrationTest {
    @get:Rule val app = createAndroidComposeRule<MainActivity>()
    @Test fun real_tls_pairing_audio_camera_and_remote_commands() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val invitation = context.getFileStreamPath("test-pairing.txt")
        assumeTrue("Run tools/test_backend.ps1 to start the isolated receiver.", invitation.exists())
        instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.RECORD_AUDIO)
        instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.CAMERA)
        app.onNodeWithContentDescription("Connection details").performClick()
        val wrongIdentity = invitation.readText().replace(Regex("pin=[A-Fa-f0-9]{64}"), "pin=" + "0".repeat(64))
        app.onNodeWithText("Pairing link").performTextInput(wrongIdentity)
        app.onNodeWithText("Connect", substring = false).performClick()
        app.waitUntil(15000) { app.onAllNodesWithText("PC identity does not match", substring = true).fetchSemanticsNodes().isNotEmpty() }
        app.onNodeWithText("Pairing link").performTextReplacement(invitation.readText())
        app.onNodeWithText("Connect", substring = false).performScrollTo().performClick()
        try {
            app.waitUntil(15000) { app.onAllNodesWithText("Connected to ", substring = true).fetchSemanticsNodes().isNotEmpty() }
        } catch (error: Exception) {
            val device = (app.activity.application as SynqoraApplication).state.session.device.value
            throw AssertionError("Pairing retry: ${device.phase}; ${device.error}", error)
        }
        app.onNodeWithText("Done", substring = false).performClick()
        fun shell(command: String) = android.os.ParcelFileDescriptor.AutoCloseInputStream(instrumentation.uiAutomation.executeShellCommand(command)).bufferedReader().use { it.readText() }
        shell("dumpsys gfxinfo com.synqora reset")

        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithText("Start microphone", substring = false).performScrollTo().performClick()
        app.waitUntil(5000) { app.onAllNodesWithText("Streaming to ", substring = true).fetchSemanticsNodes().isNotEmpty() }
        Thread.sleep(1200) // Real AudioRecord + network time, not animation-clock time.
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Webcam", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Start camera").performScrollTo().performClick()
        app.waitUntil(20000) { app.onAllNodesWithText("Streaming to PC", substring = false).fetchSemanticsNodes().isNotEmpty() }
        Thread.sleep(2500) // CameraX analyzer produces real emulated-camera frames.
        lateinit var model: com.synqora.data.SynqoraState
        app.runOnIdle { model = (app.activity.application as SynqoraApplication).state }
        app.waitUntil(10000) { model.session.device.value.receivedVideoFrames >= 5 }
        // Activity backgrounding and display sleep must not own the media lifetime.
        app.runOnUiThread { app.activity.moveTaskToBack(true) }
        shell("input keyevent 223")
        try {
            Thread.sleep(1500)
            val sleepingAudio = model.session.device.value.receivedAudioBytes
            val sleepingFrames = model.session.device.value.receivedVideoFrames
            Thread.sleep(2500)
            check(model.microphoneRunning && model.camera.state.value.active) { "Capture stopped on display sleep" }
            check(model.session.device.value.phase == com.synqora.data.ConnectionPhase.Connected) { "Screen-off disconnected the session" }
            check(model.session.device.value.receivedAudioBytes > sleepingAudio) { "Audio did not reach the PC while the display slept" }
            check(model.session.device.value.receivedVideoFrames > sleepingFrames) { "Video did not reach the PC while the display slept" }
        } finally {
            shell("input keyevent 224")
            shell("wm dismiss-keyguard")
            instrumentation.runOnMainSync {
                app.activity.startActivity(android.content.Intent(app.activity, MainActivity::class.java).addFlags(android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            }
        }
        repeat(4) {
            app.onNodeWithContentDescription("Hide controls").performScrollTo().performClick()
            app.onNodeWithContentDescription("Show controls").performScrollTo().performClick()
        }
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Keyboard", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Live keyboard").assertExists()
        app.runOnIdle {
            fun locate(view: android.view.View): com.synqora.input.LiveKeyboardView? {
                if (view is com.synqora.input.LiveKeyboardView) return view
                if (view is android.view.ViewGroup) for (i in 0 until view.childCount) locate(view.getChildAt(i))?.let { return it }
                return null
            }
            val keyboard = checkNotNull(locate(app.activity.window.decorView))
            val editor = keyboard.onCreateInputConnection(android.view.inputmethod.EditorInfo())
            "Synqora backend test".forEach { editor.commitText(it.toString(), 1) }
            editor.deleteSurroundingTextInCodePoints(1, 0); editor.commitText("t", 1)
            editor.performEditorAction(android.view.inputmethod.EditorInfo.IME_ACTION_DONE)
            keyboard.sendKey("tab")
        }
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Touchpad", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Remote touchpad").performScrollTo().performTouchInput { swipe(Offset(80f, 80f), Offset(240f, 150f), 300) }
        app.onNodeWithContentDescription("Remote touchpad").performTouchInput {
            down(0, Offset(100f, 100f)); down(1, Offset(200f, 100f))
            moveBy(0, Offset(0f, 80f)); moveBy(1, Offset(0f, 80f)); up(0); up(1)
        }
        app.onNodeWithText("Left click", substring = false).performScrollTo().performClick()

        val previousFrames = model.session.device.value.receivedVideoFrames
        app.waitUntil(10000) { model.session.device.value.receivedVideoFrames >= previousFrames + 5 }
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Webcam", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Stop camera").performScrollTo().performClick()
        app.waitUntil(5000) { !model.camera.state.value.active }
        Thread.sleep(800)
        val timing = shell("dumpsys gfxinfo com.synqora framestats")
        android.util.Log.i("SynqoraFrameTimes", timing.substringBefore("---PROFILEDATA---"))
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithText("Stop", substring = false).performScrollTo().performClick()
        app.onNodeWithContentDescription("Connection details").performClick()
        app.onNodeWithText("Disconnect", substring = false).performClick()
        app.waitUntil(5000) { app.onAllNodesWithText("Connect", substring = false).fetchSemanticsNodes().isNotEmpty() }
        app.onNodeWithText("Pairing link").performTextReplacement(invitation.readText())
        app.onNodeWithText("Connect", substring = false).performClick()
        app.waitUntil(10000) { app.onAllNodesWithText("Connected to ", substring = true).fetchSemanticsNodes().isNotEmpty() }
        app.onNodeWithText("Enable remote controls on your PC", substring = true).assertExists()
        app.onNodeWithText("Disconnect", substring = false).performClick()
    }
}
