package com.synqora.media

import android.content.Context
import android.graphics.ImageFormat
import android.graphics.Rect
import android.graphics.YuvImage
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CaptureRequest
import android.hardware.display.DisplayManager
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.os.Build
import android.os.PowerManager
import android.content.BroadcastReceiver
import android.content.Intent
import android.content.IntentFilter
import android.util.Size
import android.util.Range
import android.view.Display
import android.view.Surface
import android.view.OrientationEventListener
import androidx.camera.camera2.interop.Camera2CameraInfo
import androidx.camera.camera2.interop.Camera2Interop
import androidx.camera.camera2.interop.ExperimentalCamera2Interop
import androidx.camera.core.*
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.AspectRatioStrategy
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.content.ContextCompat
import androidx.lifecycle.LifecycleOwner
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors

data class CameraState(val active: Boolean = false, val ready: Boolean = false, val error: String? = null, val targetFps: Int = 0, val actualFps: Int = 0, val width: Int = 0, val height: Int = 0, val encodeMs: Int = 0)

/** Session-service-owned capture; a page owns only its preview surface, never the encoder. */
@androidx.annotation.OptIn(markerClass = [ExperimentalCamera2Interop::class])
class CameraCapture(context: Context, private val sendFrame: (ByteArray, Int) -> Unit, private val beginSession: () -> Unit = {}) {
    private val app = context.applicationContext
    private val main = ContextCompat.getMainExecutor(app)
    private val power = app.getSystemService(PowerManager::class.java)
    private var limited = power.isPowerSaveMode || power.currentThermalStatus >= PowerManager.THERMAL_STATUS_SEVERE
    private val thermalListener = PowerManager.OnThermalStatusChangedListener { main.execute { powerChanged() } }
    private val powerReceiver = object : BroadcastReceiver() { override fun onReceive(context: Context?, intent: Intent?) { powerChanged() } }
    private val encoder = Executors.newSingleThreadExecutor()
    private val _state = MutableStateFlow(CameraState())
    val state = _state.asStateFlow()
    @Volatile var transmit = false
    @Volatile private var generation = 0
    private var owner: LifecycleOwner? = null
    private var provider: ProcessCameraProvider? = null
    private var analysis: ImageAnalysis? = null
    private var preview: Preview? = null
    private var view: PreviewView? = null
    private var lens = CameraSelector.DEFAULT_FRONT_CAMERA
    private var front = true
    var mode = "Smooth · 60 fps"; private set
    private var fpsRange = Range(30, 30)
    private var closed = false
    private val displays = app.getSystemService(DisplayManager::class.java)
    private fun displayRotation() = displays.getDisplay(Display.DEFAULT_DISPLAY)?.rotation ?: Surface.ROTATION_0
    private var capturePose = displayRotation()
    private val orientation = object : OrientationEventListener(app) {
        override fun onOrientationChanged(degrees: Int) {
            val rotation = captureRotation(degrees, capturePose)
            if (rotation != capturePose) {
                capturePose = rotation
                analysis?.targetRotation = rotation
            }
        }
    }
    private val rotationListener = object : DisplayManager.DisplayListener {
        override fun onDisplayAdded(displayId: Int) = Unit
        override fun onDisplayRemoved(displayId: Int) = Unit
        override fun onDisplayChanged(displayId: Int) {
            if (displayId == Display.DEFAULT_DISPLAY) {
                val rotation = displayRotation()
                if (!orientation.canDetectOrientation()) { capturePose = rotation; analysis?.targetRotation = rotation }
                preview?.targetRotation = rotation
            }
        }
    }
    init {
        displays.registerDisplayListener(rotationListener, Handler(Looper.getMainLooper()))
        power.addThermalStatusListener(thermalListener)
        ContextCompat.registerReceiver(app, powerReceiver, IntentFilter(PowerManager.ACTION_POWER_SAVE_MODE_CHANGED), ContextCompat.RECEIVER_NOT_EXPORTED)
    }
    private fun powerChanged() {
        if (closed) return
        if (power.currentThermalStatus >= PowerManager.THERMAL_STATUS_CRITICAL && _state.value.active) { stop(); _state.value = CameraState(error = "Camera stopped because this phone is too hot. Let it cool before restarting."); return }
        val next = power.isPowerSaveMode || power.currentThermalStatus >= PowerManager.THERMAL_STATUS_SEVERE
        if (next != limited) { limited = next; if (_state.value.active) bind() }
    }

    fun attach(lifecycle: LifecycleOwner) {
        if (owner === lifecycle) return
        releaseUseCases(); owner = lifecycle
        if (_state.value.active) bind()
    }
    fun detach(lifecycle: LifecycleOwner) {
        if (owner !== lifecycle) return
        generation++; releaseUseCases(); owner = null; view = null
    }
    fun start() {
        if (closed || _state.value.active) return
        beginSession()
        if (orientation.canDetectOrientation()) orientation.enable()
        _state.value = CameraState(active = true)
        bind()
    }
    fun stop() {
        orientation.disable()
        generation++; transmit = false; releaseUseCases()
        _state.value = CameraState()
    }
    fun close() { closed = true; stop(); displays.unregisterDisplayListener(rotationListener); power.removeThermalStatusListener(thermalListener); app.unregisterReceiver(powerReceiver); encoder.shutdown() }
    fun mode(value: String) { if (value == mode) return; mode = value; if (_state.value.active) bind() }
    fun flip() { front = !front; if (_state.value.active) bind() }
    fun attachPreview(target: PreviewView) {
        view = target
        if (analysis != null) bindPreview()
    }
    fun detachPreview(target: PreviewView) {
        if (view !== target) return
        preview?.let { provider?.unbind(it) }; preview = null; view = null
        // ImageAnalysis remains bound, so changing pages does not stop capture or transport.
    }
    private fun releaseUseCases() {
        analysis?.clearAnalyzer()
        val cases = listOfNotNull(analysis, preview).toTypedArray()
        if (cases.isNotEmpty()) provider?.unbind(*cases)
        analysis = null; preview = null
    }
    private fun bindPreview() {
        val lifecycle = owner ?: return
        val target = view ?: return
        val cameras = provider ?: return
        if (preview != null) return
        try {
            val useCase = Preview.Builder().setTargetRotation(displayRotation()).setTargetFrameRate(fpsRange).build()
            useCase.setSurfaceProvider(target.surfaceProvider)
            cameras.bindToLifecycle(lifecycle, lens, useCase)
            preview = useCase
        } catch (_: Exception) {
            // The camera stream can continue even when a device rejects an extra preview surface.
            _state.value = _state.value.copy(error = "Preview unavailable. Camera capture is still active.")
        }
    }
    private fun bind() {
        val lifecycle = owner ?: return
        val serial = ++generation
        releaseUseCases()
        val future = ProcessCameraProvider.getInstance(app)
        future.addListener({
            if (closed || serial != generation || !_state.value.active || owner !== lifecycle) return@addListener
            try {
                val cameras = future.get(); provider = cameras
                val wanted = if (front) CameraSelector.DEFAULT_FRONT_CAMERA else CameraSelector.DEFAULT_BACK_CAMERA
                lens = if (cameras.hasCamera(wanted)) wanted else if (front) CameraSelector.DEFAULT_BACK_CAMERA else CameraSelector.DEFAULT_FRONT_CAMERA
                val info = lens.filter(cameras.availableCameraInfos).first()
                val requestedFps = if (limited) 30 else if (mode.contains("60")) 60 else 30
                val ranges = Camera2CameraInfo.from(info).getCameraCharacteristic(CameraCharacteristics.CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES)?.toList().orEmpty()
                fpsRange = ranges.filter { it.upper <= requestedFps }.maxWithOrNull(compareBy<Range<Int>> { it.upper }.thenBy { it.lower }) ?: ranges.minByOrNull { it.upper } ?: Range(30, 30)
                val resolution = if (limited || mode.startsWith("Economy")) Size(640, 360) else Size(1280, 720)
                val selector = ResolutionSelector.Builder().setAspectRatioStrategy(AspectRatioStrategy.RATIO_16_9_FALLBACK_AUTO_STRATEGY).setResolutionStrategy(ResolutionStrategy(resolution, ResolutionStrategy.FALLBACK_RULE_CLOSEST_LOWER_THEN_HIGHER)).build()
                // Keep sensor YUV until after the transmission/rate gate. RGBA output makes
                // CameraX convert even discarded frames and local-preview-only analysis.
                val builder = ImageAnalysis.Builder().setTargetRotation(capturePose).setResolutionSelector(selector).setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_YUV_420_888)
                Camera2Interop.Extender(builder).setCaptureRequestOption(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, fpsRange)
                val capture = builder.build()
                val target = minOf(requestedFps, fpsRange.upper)
                val period = 1_000_000_000L / target
                var next = 0L
                var ready = false
                var nv21 = ByteArray(0)
                val output = ByteArrayOutputStream(256 * 1024)
                var reportAt = SystemClock.elapsedRealtimeNanos()
                var frames = 0
                var encodingNanos = 0L
                capture.setAnalyzer(encoder) { image ->
                    try {
                        if (serial != generation) return@setAnalyzer
                        if (!ready) {
                            ready = true
                            main.execute { if (serial == generation) _state.value = _state.value.copy(ready = true, targetFps = target, width = image.width, height = image.height, error = null) }
                        }
                        val now = SystemClock.elapsedRealtimeNanos()
                        if (!transmit || now < next - 1_000_000) return@setAnalyzer
                        next = maxOf(next + period, now + period / 2)
                        val started = SystemClock.elapsedRealtimeNanos()
                        val width = image.width; val height = image.height
                        val length = width * height * 3 / 2
                        if (nv21.size != length) nv21 = ByteArray(length)
                        packNv21(image, nv21)
                        output.reset()
                        check(YuvImage(nv21, ImageFormat.NV21, width, height, null).compressToJpeg(Rect(0, 0, width, height), if (mode.startsWith("Detail")) 85 else 75, output))
                        if (serial == generation && transmit) sendFrame(output.toByteArray(), image.imageInfo.rotationDegrees)
                        frames++; encodingNanos += SystemClock.elapsedRealtimeNanos() - started
                        if (now - reportAt >= 1_000_000_000) {
                            val fps = (frames * 1_000_000_000L / (now - reportAt)).toInt()
                            val encode = (encodingNanos / frames / 1_000_000).toInt()
                            main.execute { if (serial == generation) _state.value = _state.value.copy(actualFps = fps, encodeMs = encode) }
                            frames = 0; encodingNanos = 0; reportAt = now
                        }
                    } catch (_: Exception) {
                        main.execute { if (serial == generation) { stop(); _state.value = CameraState(error = "Camera capture stopped. Close other camera apps and try again.") } }
                    } finally { image.close() }
                }
                analysis = capture
                cameras.bindToLifecycle(lifecycle, lens, capture)
                bindPreview()
            } catch (_: Exception) {
                stop(); _state.value = CameraState(error = "Camera unavailable. Close other camera apps and try again.")
            }
        }, main)
    }
    private fun packNv21(image: ImageProxy, destination: ByteArray) {
        val w = image.width; val h = image.height
        val y = image.planes[0]; val yBuffer = y.buffer.duplicate(); val yStart = yBuffer.position()
        for (row in 0 until h) {
            if (y.pixelStride == 1) { yBuffer.position(yStart + row * y.rowStride); yBuffer.get(destination, row * w, w) }
            else for (column in 0 until w) destination[row * w + column] = yBuffer.get(yStart + row * y.rowStride + column * y.pixelStride)
        }
        val u = image.planes[1]; val v = image.planes[2]
        val ub = u.buffer.duplicate(); val vb = v.buffer.duplicate(); val uStart = ub.position(); val vStart = vb.position()
        for (row in 0 until h / 2) for (column in 0 until w / 2) {
            val at = w * h + row * w + column * 2
            destination[at] = vb.get(vStart + row * v.rowStride + column * v.pixelStride)
            destination[at + 1] = ub.get(uStart + row * u.rowStride + column * u.pixelStride)
        }
    }
}
