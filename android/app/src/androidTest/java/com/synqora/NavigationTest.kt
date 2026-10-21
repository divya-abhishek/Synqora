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
        app.onAllNodesWithText("Keyboard", useUnmergedTree = true).onLast().performScrollTo().performClick()
        app.onNodeWithContentDescription("Backspace").assertIsNotEnabled()
        app.onNodeWithText("Send to computer").assertDoesNotExist()
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onAllNodesWithText("Microphone", useUnmergedTree = true).onLast().performClick()
        app.onNodeWithContentDescription("Mute").performScrollTo().performClick()
        app.onNodeWithContentDescription("Unmute").assertExists()
        app.onNodeWithContentDescription("Input gain").performScrollTo().performSemanticsAction(SemanticsActions.SetProgress) { it(.4f) }
        app.onNodeWithText("40%").assertExists()
        app.onNodeWithContentDescription("Back to Home").performClick()
        app.onNodeWithText("Settings", substring = false).performClick()
        app.onNodeWithContentDescription("Reduce motion").performScrollTo().performClick()
        app.onNodeWithContentDescription("Reduce motion").assertIsOn()
    }
}
