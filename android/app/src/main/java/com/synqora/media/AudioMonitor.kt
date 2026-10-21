package com.synqora.media

import android.Manifest
import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlin.math.abs

// Audio reads occur on IO, never in composition or a rendering callback.
// The UI receives a bounded 64-value snapshot at no more than 30 Hz.
class AudioMonitor {
    private val _levels = MutableStateFlow(List(64) { 0f })
    val levels = _levels.asStateFlow()
    private var job: Job? = null
    private var recorder: AudioRecord? = null
    @Volatile var gain = 1f
    @Volatile var muted = false
    @SuppressLint("MissingPermission")
    fun start(scope: CoroutineScope, onError: (String) -> Unit, onPcm: (ByteArray) -> Unit = {}) {
        if (job?.isActive == true) return
        job = scope.launch(Dispatchers.IO) {
            try {
                val size = AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT).coerceAtLeast(4096)
                val input = AudioRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, 48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, size)
                recorder = input
                check(input.state == AudioRecord.STATE_INITIALIZED) { "Microphone is unavailable." }
                input.startRecording()
                val samples = ShortArray(1600)
                val history = ArrayDeque<Float>(64).also { repeat(64) { _ -> it.add(0f) } }
                var lastPublish = 0L
                while (isActive) {
                    val count = input.read(samples, 0, samples.size)
                    check(count > 0) { "Audio input stopped. Try the microphone again." }
                    var peak = 0f
                    for (i in 0 until count) peak = maxOf(peak, abs(samples[i].toInt()) / 32768f)
                    val pcm = ByteArray(count * 2)
                    for (i in 0 until count) {
                        val v = if (muted) 0 else (samples[i] * gain).toInt().coerceIn(-32768, 32767)
                        pcm[i * 2] = v.toByte(); pcm[i * 2 + 1] = (v shr 8).toByte()
                    }
                    onPcm(pcm)
                    history.removeFirst(); history.addLast(peak)
                    val now = android.os.SystemClock.elapsedRealtime()
                    if (now - lastPublish >= 33) { _levels.value = history.toList(); lastPublish = now }
                }
            } catch (e: Exception) {
                if (e !is CancellationException) withContext(Dispatchers.Main) { onError(e.message ?: "Microphone could not start.") }
            } finally {
                try { recorder?.stop() } catch (_: IllegalStateException) { }
                recorder?.release(); recorder = null; _levels.value = List(64) { 0f }
            }
        }
    }
    suspend fun stop() { job?.cancelAndJoin(); job = null }
}
