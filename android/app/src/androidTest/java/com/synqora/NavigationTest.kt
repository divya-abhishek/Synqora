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
        app.onNodeWithText("Activity", substring = false).performClick()
        app.onNodeWithText("Sync notifications").assertExists()
        app.onNodeWithText("Files", substring = false).performClick()
        app.onNodeWithText("Choose a file").assertIsNotEnabled()
        app.onNodeWithText("Home", substring = false).performClick()
        app.onAllNodesWithText("Keyboard", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Backspace").assertIsNotEnabled()
        app.onNodeWithText("Send to computer").assertDoesNotExist()
        app.onNodeWithContentDescription("Back", substring = false).performClick()
        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithContentDescription("Mute").performScrollTo().assertIsNotEnabled()
        app.onNodeWithText("Start microphone").performScrollTo().assertIsNotEnabled()
        app.onNodeWithContentDescription("Input gain").performScrollTo().performSemanticsAction(SemanticsActions.SetProgress) { it(.4f) }
        app.onNodeWithText("40%").assertExists()
        app.onNodeWithContentDescription("Back", substring = false).performClick()
        app.onNodeWithContentDescription("Settings").performClick()
        app.onNodeWithText("Color theme").assertDoesNotExist()
        app.onNodeWithText("Theme").assertDoesNotExist()
        app.onNodeWithContentDescription("Reduce motion").performScrollTo().performClick()
        app.onNodeWithContentDescription("Reduce motion").assertIsOn()
    }
}
