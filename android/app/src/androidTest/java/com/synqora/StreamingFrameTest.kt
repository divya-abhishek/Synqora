package com.synqora

import android.Manifest
import android.graphics.Rect
import android.os.Bundle
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.util.Log
import android.view.InputDevice
import android.view.MotionEvent
import android.view.accessibility.AccessibilityNodeInfo
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assume.assumeTrue
import org.junit.Test

/** Wall-clock input and platform frames, without ComposeTestRule's virtual animation clock. */
class StreamingFrameTest {
    @Test fun actual_input_during_simultaneous_streaming() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val invitation = context.getFileStreamPath("test-pairing.txt")
        assumeTrue("Run tools/test_backend.ps1 -PerformanceOnly.", invitation.exists())
        val ui = instrumentation.uiAutomation
        ui.grantRuntimePermission(context.packageName, Manifest.permission.RECORD_AUDIO)
        ui.grantRuntimePermission(context.packageName, Manifest.permission.CAMERA)
        fun find(label: String, description: Boolean = false, partial: Boolean = false, visibleOnly: Boolean = true): AccessibilityNodeInfo {
            val until = SystemClock.uptimeMillis() + 20000
            while (SystemClock.uptimeMillis() < until) {
                fun scan(node: AccessibilityNodeInfo?): AccessibilityNodeInfo? {
                    if (node == null) return null
                    for (i in node.childCount - 1 downTo 0) scan(node.getChild(i))?.let { return it }
                    val value = (if (description) node.contentDescription else node.text)?.toString().orEmpty()
                    return node.takeIf { (!visibleOnly || it.isVisibleToUser) && if (partial) value.contains(label) else value == label }
                }
                scan(ui.rootInActiveWindow)?.let { return it }
                Thread.sleep(100)
            }
            error("Control did not appear: $label")
        }
        fun tap(label: String, description: Boolean = false) {
            val node = find(label, description)
            val bounds = Rect(); node.getBoundsInScreen(bounds)
            val now = SystemClock.uptimeMillis()
            for ((action, time) in listOf(MotionEvent.ACTION_DOWN to now, MotionEvent.ACTION_UP to now + 12)) {
                val event = MotionEvent.obtain(now, time, action, bounds.exactCenterX(), bounds.exactCenterY(), 0)
                event.source = InputDevice.SOURCE_TOUCHSCREEN
                check(ui.injectInputEvent(event, true)); event.recycle()
            }
            Thread.sleep(380)
        }
        fun shell(command: String) = ParcelFileDescriptor.AutoCloseInputStream(ui.executeShellCommand(command)).bufferedReader().use { it.readText() }
        fun select(label: String) {
            // Setup controls may be inside a scrolling page. Use their platform accessibility
            // action; measured bottom-navigation taps below still inject actual touch events.
            var node = find(label, visibleOnly = false)
            node.performAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_SHOW_ON_SCREEN.id)
            while (!node.isClickable && node.parent != null) node = node.parent
            check(node.performAction(AccessibilityNodeInfo.ACTION_CLICK)) { "Control could not be activated: $label" }
            Thread.sleep(600)
        }
        ActivityScenario.launch(MainActivity::class.java).use {
            tap("Connection details", description = true)
            var input = find("Pairing link")
            while (!input.isEditable && input.parent != null) input = input.parent
            check(input.isEditable) { "Pairing text field is not exposed as editable" }
            check(input.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, Bundle().apply { putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, invitation.readText()) }))
            select("Connect")
            find("Connected to ", partial = true); tap("Done")
            select("Microphone"); select("Start microphone")
            find("Streaming to ", partial = true)
            tap("Back to Home", description = true); select("Webcam"); tap("Start camera", description = true)
            find("Streaming to PC"); Thread.sleep(2500)
            it.onActivity { activity ->
                val state = (activity.application as SynqoraApplication).state.camera.state.value
                Log.i("SynqoraCaptureMetrics", "target=${state.targetFps} encoded=${state.actualFps} resolution=${state.width}x${state.height} encodeMs=${state.encodeMs}")
            }
            // Keep camera analysis + audio + TLS active while exercising real navigation.
            tap("Back to Home", description = true); tap("Settings"); tap("Home")
            shell("dumpsys gfxinfo com.synqora reset")
            repeat(12) { tap("Settings"); tap("Home") }
            shell("dumpsys gfxinfo com.synqora framestats").lineSequence().forEach { line -> Log.i("SynqoraFrameTimes", line) }
            select("Webcam"); tap("Stop camera", description = true)
            tap("Back to Home", description = true); select("Microphone"); select("Stop")
        }
    }
}
