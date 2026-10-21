package com.synqora.design

import android.app.Activity
import android.os.Handler
import android.os.Looper
import android.view.FrameMetrics
import android.view.Window
import androidx.compose.runtime.*
import androidx.activity.compose.LocalActivity

/** Passive measurements of frames already rendered; never schedules idle frames. */
@Suppress("DEPRECATION")
@Composable fun rememberRenderPressure(quality: String): Boolean {
    val activity = LocalActivity.current
    var pressure by remember(quality) { mutableStateOf(false) }
    DisposableEffect(activity, quality) {
        val recent = ArrayDeque<Boolean>(60)
        var seen = 0
        var missed = 0
        val refresh = activity?.windowManager?.defaultDisplay?.refreshRate?.coerceAtLeast(60f) ?: 60f
        val slowThreshold = (2_000_000_000.0 / refresh).toLong()
        val listener = Window.OnFrameMetricsAvailableListener { _, metrics, _ ->
            if (++seen > 30 && !pressure) { // Skip startup; require sustained pressure.
                val gpu = if (android.os.Build.VERSION.SDK_INT >= 31) metrics.getMetric(FrameMetrics.GPU_DURATION).coerceAtLeast(0) else 0
                val slow = maxOf(metrics.getMetric(FrameMetrics.TOTAL_DURATION), gpu) > slowThreshold
                if (recent.size == 60 && recent.removeFirst()) missed--
                recent.addLast(slow); if (slow) missed++
                if (recent.size == 60 && missed >= 20) pressure = true
            }
        }
        activity?.window?.addOnFrameMetricsAvailableListener(listener, Handler(Looper.getMainLooper()))
        onDispose { activity?.window?.removeOnFrameMetricsAvailableListener(listener) }
    }
    return pressure
}
