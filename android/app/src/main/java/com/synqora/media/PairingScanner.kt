package com.synqora.media

import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.zxing.BinaryBitmap
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.ReaderException
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeReader
import java.util.concurrent.Executors

/** Local QR decoding. No cloud service, account, or downloaded recognition model. */
@Composable fun PairingScanner(modifier: Modifier, onInvitation: (String) -> Unit, onError: (String) -> Unit) {
    val context = LocalContext.current
    val owner = LocalLifecycleOwner.current
    val accept by rememberUpdatedState(onInvitation)
    val error by rememberUpdatedState(onError)
    val view = remember { PreviewView(context).apply { implementationMode = PreviewView.ImplementationMode.COMPATIBLE } }
    DisposableEffect(owner) {
        val future = ProcessCameraProvider.getInstance(context)
        val preview = Preview.Builder().build()
        val analysis = ImageAnalysis.Builder().setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
        val worker = Executors.newSingleThreadExecutor()
        val main = ContextCompat.getMainExecutor(context)
        val reader = QRCodeReader()
        var provider: ProcessCameraProvider? = null
        var closed = false
        var accepted = false
        var previous = 0L
        analysis.setAnalyzer(worker) { image ->
            try {
                val now = android.os.SystemClock.elapsedRealtime()
                if (!accepted && now - previous >= 150) {
                    previous = now
                    val plane = image.planes[0]
                    val bytes = ByteArray(image.width * image.height)
                    // Image planes can contain padding at the end of every row.
                    for (row in 0 until image.height) {
                        plane.buffer.position(row * plane.rowStride)
                        plane.buffer.get(bytes, row * image.width, image.width)
                    }
                    val source = PlanarYUVLuminanceSource(bytes, image.width, image.height, 0, 0, image.width, image.height, false)
                    val invitation = reader.decode(BinaryBitmap(HybridBinarizer(source))).text
                    if (invitation.startsWith("synqora://pair?")) {
                        accepted = true
                        main.execute { if (!closed) accept(invitation) }
                    }
                }
            } catch (_: ReaderException) { /* No code in this frame. */ }
            catch (_: Exception) { main.execute { if (!closed) error("Could not read the camera. Paste the PC pairing link instead.") } }
            finally { reader.reset(); image.close() }
        }
        future.addListener({ if (!closed) try {
            provider = future.get(); preview.setSurfaceProvider(view.surfaceProvider)
            val lens = if (provider!!.hasCamera(CameraSelector.DEFAULT_BACK_CAMERA)) CameraSelector.DEFAULT_BACK_CAMERA else CameraSelector.DEFAULT_FRONT_CAMERA
            provider!!.bindToLifecycle(owner, lens, preview, analysis)
        } catch (_: Exception) { error("Camera unavailable. Paste the PC pairing link instead.") } }, main)
        onDispose { closed = true; analysis.clearAnalyzer(); provider?.unbind(preview, analysis); worker.shutdown() }
    }
    AndroidView({ view }, modifier)
}
