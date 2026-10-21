package com.synqora.data

import android.Manifest
import android.app.Application
import android.content.ContentResolver
import android.content.ContentUris
import android.content.pm.PackageManager
import android.database.ContentObserver
import android.graphics.Bitmap
import android.net.Uri
import android.os.*
import android.provider.MediaStore
import android.util.LruCache
import android.util.Size
import androidx.core.content.ContextCompat
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.util.UUID

/** On-demand, paged gallery. No camera and no periodic media scan. */
class GallerySync(private val app: Application, private val session: LanSession, private val companion: Companion, private val scope: CoroutineScope) {
    private val prefs = app.getSharedPreferences("synqora", 0)
    val enabled = MutableStateFlow(prefs.getBoolean("gallery_sync", false))
    val access = MutableStateFlow("Not shared")
    private val requests = Channel<JSONObject>(32)
    private val assets = linkedMapOf<String, Uri>()
    private val ids = linkedMapOf<String, String>()
    private val thumbnails = object : LruCache<String, ByteArray>(1024 * 1024) { override fun sizeOf(key: String, value: ByteArray) = value.size }
    private var observing = false
    private var changes: Job? = null
    private val observer = object : ContentObserver(Handler(Looper.getMainLooper())) {
        override fun onChange(selfChange: Boolean) { changes?.cancel(); changes = scope.launch { delay(1000); thumbnails.evictAll(); status(); session.companion(JSONObject().put("type", "galleryChanged")) } }
    }
    init {
        companion.onGallery = { requests.trySend(it) }
        scope.launch {
            combine(session.device.map { it.phase }.distinctUntilChanged(), enabled) { phase, on -> phase == ConnectionPhase.Connected && on }.collect { on ->
                if (on) refreshAccess() else { if (observing) app.contentResolver.unregisterContentObserver(observer); observing = false; synchronized(assets) { assets.clear(); ids.clear() }; thumbnails.evictAll(); status() }
            }
        }
        scope.launch(Dispatchers.IO) { for (event in requests) {
            try {
                if (!enabled.value || !hasAccess() || session.device.value.phase != ConnectionPhase.Connected) { status(); continue }
                when (event.optString("type")) {
                    "galleryPage" -> page(event.optInt("offset", 0).coerceIn(0, 1_000_000))
                    "galleryThumb" -> thumbnail(event.getString("id"))
                    "galleryOriginal" -> {
                        val request = event.getString("request"); require(request.matches(Regex("[a-fA-F0-9]{32}")))
                        val uri = synchronized(assets) { assets[event.getString("id")] } ?: error("Photo is no longer available.")
                        companion.offer(uri, request)
                    }
                }
            } catch (_: Exception) { session.companion(JSONObject().put("type", "galleryError").put("message", "Photo access changed or this item is unavailable. Refresh or check access on your phone.")) }
        } }
    }
    fun sharing(value: Boolean) { enabled.value = value; prefs.edit().putBoolean("gallery_sync", value).apply(); refreshAccess() }
    fun hasAccess(): Boolean = if (Build.VERSION.SDK_INT >= 33) granted(Manifest.permission.READ_MEDIA_IMAGES) || granted(Manifest.permission.READ_MEDIA_VIDEO) || Build.VERSION.SDK_INT >= 34 && granted(Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED) else granted(Manifest.permission.READ_EXTERNAL_STORAGE)
    private fun granted(permission: String) = ContextCompat.checkSelfPermission(app, permission) == PackageManager.PERMISSION_GRANTED
    fun refreshAccess() {
        access.value = when {
            !enabled.value -> "Not shared"
            !hasAccess() -> "Choose photo access"
            Build.VERSION.SDK_INT >= 34 && !granted(Manifest.permission.READ_MEDIA_IMAGES) && !granted(Manifest.permission.READ_MEDIA_VIDEO) -> "Selected photos and videos"
            else -> "Photo library available"
        }
        if (enabled.value && hasAccess() && session.device.value.phase == ConnectionPhase.Connected && !observing) {
            app.contentResolver.registerContentObserver(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, true, observer)
            app.contentResolver.registerContentObserver(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, true, observer); observing = true
        } else if ((!enabled.value || !hasAccess()) && observing) { app.contentResolver.unregisterContentObserver(observer); observing = false }
        status()
    }
    private fun status() { session.companion(JSONObject().put("type", "galleryStatus").put("enabled", enabled.value && hasAccess()).put("access", access.value)) }
    private fun page(offset: Int) {
        val items = JSONArray()
        val projection = arrayOf(MediaStore.Files.FileColumns._ID, MediaStore.Files.FileColumns.DISPLAY_NAME, MediaStore.Files.FileColumns.SIZE, MediaStore.Files.FileColumns.MIME_TYPE, MediaStore.Files.FileColumns.DATE_ADDED, MediaStore.Files.FileColumns.MEDIA_TYPE)
        val args = Bundle().apply {
            putStringArray(ContentResolver.QUERY_ARG_SORT_COLUMNS, arrayOf(MediaStore.Files.FileColumns.DATE_ADDED, MediaStore.Files.FileColumns._ID))
            putInt(ContentResolver.QUERY_ARG_SORT_DIRECTION, ContentResolver.QUERY_SORT_DIRECTION_DESCENDING)
            putInt(ContentResolver.QUERY_ARG_LIMIT, 25); putInt(ContentResolver.QUERY_ARG_OFFSET, offset)
            putString(ContentResolver.QUERY_ARG_SQL_SELECTION, "${MediaStore.Files.FileColumns.MEDIA_TYPE} IN (?, ?) AND ${MediaStore.MediaColumns.IS_PENDING} = 0")
            putStringArray(ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS, arrayOf(MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE.toString(), MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO.toString()))
        }
        var more = false
        app.contentResolver.query(MediaStore.Files.getContentUri("external"), projection, args, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                if (items.length() == 24) { more = true; break }
                val video = cursor.getInt(5) == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO
                val uri = ContentUris.withAppendedId(if (video) MediaStore.Video.Media.EXTERNAL_CONTENT_URI else MediaStore.Images.Media.EXTERNAL_CONTENT_URI, cursor.getLong(0))
                val id = synchronized(assets) {
                    if (assets.size >= 240) { val expired = assets.keys.first(); assets.remove(expired); ids.entries.removeAll { it.value == expired } }
                    ids.getOrPut(uri.toString()) { UUID.randomUUID().toString() }.also { assets[it] = uri }
                }
                items.put(JSONObject().put("id", id).put("name", cursor.getString(1)?.take(180) ?: "Photo").put("size", cursor.getLong(2)).put("mime", cursor.getString(3)).put("time", cursor.getLong(4)).put("video", video))
            }
        }
        if (enabled.value && hasAccess()) session.companion(JSONObject().put("type", "galleryPage").put("offset", offset).put("more", more).put("items", items))
    }
    private fun thumbnail(id: String) {
        val uri = synchronized(assets) { assets[id] } ?: return
        var bytes = thumbnails.get(id)
        if (bytes == null) {
            val bitmap = app.contentResolver.loadThumbnail(uri, Size(192, 192), null)
            try { val output = ByteArrayOutputStream(); bitmap.compress(Bitmap.CompressFormat.JPEG, 65, output); bytes = output.toByteArray(); require(bytes.size <= 32768); thumbnails.put(id, bytes) }
            finally { bitmap.recycle() }
        }
        if (enabled.value && hasAccess()) session.companion(JSONObject().put("type", "galleryThumb").put("id", id).put("jpeg", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)))
    }
}
