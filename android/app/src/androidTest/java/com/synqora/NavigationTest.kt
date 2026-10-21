package com.synqora

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.semantics.SemanticsActions
import org.junit.Rule
import org.junit.Test

class NavigationTest {
    @get:Rule val app = createAndroidComposeRule<MainActivity>()

    @Test fun simplified_navigation_and_disconnected_controls() {
        app.runOnUiThread { (app.activity.application as SynqoraApplication).state.motion(false) }
        app.onNodeWithText("Tools").assertDoesNotExist()
        app.onNodeWithText("Media").assertDoesNotExist()
        app.onNodeWithText("Inbox", substring = false).performClick()
        app.onNodeWithText("Sync notifications").assertExists()
        app.onNodeWithText("Files", substring = false).performClick()
        app.onNodeWithText("Choose a file").assertIsNotEnabled()
        app.onNodeWithText("Device", substring = false).performClick()
        app.onAllNodesWithText("Keyboard", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Backspace").assertIsNotEnabled()
        app.onNodeWithText("Send to computer").assertDoesNotExist()
        app.onNodeWithContentDescription("Back to Device").performClick()
        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithContentDescription("Mute").performScrollTo().performClick()
        app.onNodeWithContentDescription("Unmute").assertExists()
        app.onNodeWithContentDescription("Input gain").performScrollTo().performSemanticsAction(SemanticsActions.SetProgress) { it(.4f) }
        app.onNodeWithText("40%").assertExists()
        app.onNodeWithContentDescription("Back to Device").performClick()
        app.onNodeWithContentDescription("Settings").performClick()
        app.onNodeWithText("Color theme").assertDoesNotExist()
        app.onNodeWithContentDescription("Reduce motion").performScrollTo().performClick()
        app.onNodeWithContentDescription("Reduce motion").assertIsOn()
    }
}
