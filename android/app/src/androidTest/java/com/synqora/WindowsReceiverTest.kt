package com.synqora

import android.Manifest
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test

class WindowsReceiverTest {
    @get:Rule val app = createAndroidComposeRule<MainActivity>()
    @Test fun captures_reach_the_native_windows_receiver() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val invitation = context.getFileStreamPath("test-pairing.txt")
        assumeTrue("Run tools/test_windows_receiver.ps1.", invitation.exists())
        instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.RECORD_AUDIO)
        instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.CAMERA)
        app.onNodeWithContentDescription("Connection details").performClick()
        app.onNodeWithText("Pairing link").performTextInput(invitation.readText())
        app.onNodeWithText("Connect", substring = false).performClick()
        app.waitUntil(15000) { app.onAllNodesWithText("Connected to ", substring = true).fetchSemanticsNodes().isNotEmpty() }
        app.onNodeWithText("Done", substring = false).performClick()

        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithText("Start microphone", substring = false).performScrollTo().performClick()
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Webcam", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithContentDescription("Start camera").performScrollTo().performClick()
        app.waitUntil(20000) { app.onAllNodesWithText("Streaming to PC", substring = false).fetchSemanticsNodes().isNotEmpty() }
        Thread.sleep(12000) // WinUI receives, decodes, renders and navigates while both capture lanes run.
        app.onNodeWithContentDescription("Stop camera").performScrollTo().performClick()
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithText("Stop", substring = false).performScrollTo().performClick()
    }
}
