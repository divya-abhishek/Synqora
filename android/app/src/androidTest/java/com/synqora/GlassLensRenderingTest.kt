package com.synqora

import android.os.Build
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.rememberGraphicsLayer
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.dp
import com.synqora.design.*
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test

class GlassLensRenderingTest {
    @get:Rule val app = createComposeRule()

    @Test fun native_lens_samples_content_on_first_draw_and_after_resize() {
        assumeTrue(Build.VERSION.SDK_INT >= 33)
        var enlarged by mutableStateOf(false)
        app.setContent {
            SynqoraTheme(true, false, false, false, rich = true) {
                val layer = rememberGraphicsLayer()
                val backdrop = remember(layer) { GlassBackdrop(layer) }
                CompositionLocalProvider(LocalBackdrop provides backdrop) {
                    Box(Modifier.size(240.dp)) {
                        Box(Modifier.matchParentSize().recordBackdrop(backdrop).background(Color.Magenta))
                        GlassLens(Modifier.align(Alignment.Center).size(if (enlarged) 160.dp else 120.dp).testTag("lens")) {}
                    }
                }
            }
        }
        fun assertSceneVisible() {
            app.waitForIdle()
            val pixels = app.onNodeWithTag("lens").captureToImage().toPixelMap()
            val center = pixels[pixels.width / 2, pixels.height / 2]
            // A frozen zero-uniform effect produces black, hiding the sampled scene.
            assertTrue("Native glass must reveal the magenta app content: $center", center.red > center.green + .10f && center.blue > center.green + .10f)
        }
        assertSceneVisible()
        app.runOnIdle { enlarged = true }
        assertSceneVisible()
    }
}
