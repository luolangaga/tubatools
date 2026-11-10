package com.tubawinui3.remote.data

import android.Manifest
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import android.widget.Toast
import androidx.core.content.ContextCompat
import com.tubawinui3.remote.ApplicationHolder
import com.tubawinui3.remote.service.NotificationHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File
import java.io.FileOutputStream
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean

enum class TransferState { Queued, Downloading, Completed, Failed, Cancelled }

@Serializable
data class DownloadTask(
    val id: String,
    val messageId: String,
    val fileName: String,
    val size: Long = 0,
    val bytes: Long = 0,
    val speed: Double = 0.0,
    val state: TransferState = TransferState.Queued,
    val error: String? = null,
    val localUri: String? = null,
    val pcId: String = "",
    val createdAt: Long = System.currentTimeMillis(),
)

/**
 * 手机端下载队列：单任务串行、断点续传、MediaStore 落盘、通知进度。
 * 进程内的 UI 与前台服务共用同一个实例。
 */
object DownloadQueue {
    private const val PREFS = "remote"
    private const val KEY_HISTORY = "downloadHistory"
    private const val MAX_TASKS = 60

    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val lock = Any()
    private val _tasks = MutableStateFlow<List<DownloadTask>>(emptyList())
    private val calls = ConcurrentHashMap<String, okhttp3.Call>()
    private val running = AtomicBoolean(false)
    private var historyLoaded = false

    val tasks: StateFlow<List<DownloadTask>> = _tasks.asStateFlow()

    private fun prefs() = ApplicationHolder.app.getSharedPreferences(PREFS, 0)

    /** 加入下载队列；同一聊天消息已完成/进行中时不会重复入队。 */
    fun enqueue(message: ChatMessage, pcId: String) {
        ensureHistory()
        val existing = _tasks.value.firstOrNull { it.messageId == message.id }
        if (existing != null && existing.state in setOf(TransferState.Queued, TransferState.Downloading, TransferState.Completed)) return
        val task = DownloadTask(
            id = UUID.randomUUID().toString().replace("-", ""),
            messageId = message.id,
            fileName = message.fileName.ifBlank { "file" },
            size = message.size,
            pcId = pcId,
        )
        synchronized(lock) { _tasks.value = (listOf(task) + _tasks.value).take(MAX_TASKS) }
        processNext()
    }

    fun retry(taskId: String) {
        ensureHistory()
        val task = _tasks.value.firstOrNull { it.id == taskId } ?: return
        if (task.state == TransferState.Downloading || task.state == TransferState.Queued) return
        update(taskId) {
            it.copy(state = TransferState.Queued, bytes = 0, speed = 0.0, error = null)
        }
        processNext()
    }

    fun cancel(taskId: String) {
        calls.remove(taskId)?.cancel()
        update(taskId) {
            if (it.state == TransferState.Completed) it
            else it.copy(state = TransferState.Cancelled, speed = 0.0)
        }
        saveHistory()
    }

    fun open(context: Context, task: DownloadTask) {
        val uri = task.localUri?.let(Uri::parse)
        if (uri == null) {
            Toast.makeText(context, "已保存到「下载/图吧工具箱」", Toast.LENGTH_LONG).show()
            return
        }
        val ext = task.fileName.substringAfterLast('.', "").lowercase()
        val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "*/*"
        val intent = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, mime)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { context.startActivity(intent) }
            .onFailure { Toast.makeText(context, "没有能打开该文件的应用", Toast.LENGTH_SHORT).show() }
    }

    /** 保存一段内存里的字节（摄像头截图等）到「下载/图吧工具箱」。 */
    fun saveBytes(context: Context, fileName: String, bytes: ByteArray): String? {
        val temp = File(context.cacheDir, "snapshots/" + UUID.randomUUID().toString().replace("-", "") + ".jpg")
        temp.parentFile?.mkdirs()
        temp.writeBytes(bytes)
        return try {
            publish(context, temp, fileName)
        } finally {
            temp.delete()
        }
    }

    // ───────────────────────── 队列执行 ─────────────────────────

    private fun processNext() {
        if (!running.compareAndSet(false, true)) return
        scope.launch {
            try {
                while (true) {
                    val next = synchronized(lock) {
                        _tasks.value.firstOrNull { it.state == TransferState.Queued }
                    } ?: break
                    download(next)
                }
            } finally {
                running.set(false)
                // 入队与“发现空队列”之间存在竞态，这里再兜一次
                if (synchronized(lock) { _tasks.value.any { it.state == TransferState.Queued } }) processNext()
            }
        }
    }

    private suspend fun download(task: DownloadTask, restarted: Boolean = false) {
        ensureHistory()
        val pc = findPc(task.pcId)
        if (pc == null) {
            fail(task, "电脑未连接，请先在 App 中连接")
            return
        }
        val ctx = ApplicationHolder.app
        val tempDir = File(ctx.cacheDir, "transfers").apply { mkdirs() }
        val temp = File(tempDir, task.id + ".part")
        val offset = if (restarted) 0L else temp.length()
        update(task.id) { it.copy(state = TransferState.Downloading, bytes = offset, error = null) }
        var lastBytes = offset
        var lastAt = System.currentTimeMillis()
        var lastNotifyAt = 0L
        publishProgress(ctx, task.id)
        try {
            FileOutputStream(temp, offset > 0).use { out ->
                val client = ApiClient(pc.host, pc.port, pc.token, pc.fingerprint)
                client.chatDownload(
                    id = task.messageId,
                    offset = offset,
                    onProgress = { received, total ->
                        val now = System.currentTimeMillis()
                        val speed = if (now > lastAt) (received - lastBytes) * 1000.0 / (now - lastAt) else 0.0
                        lastBytes = received
                        lastAt = now
                        update(task.id) {
                            it.copy(
                                bytes = received,
                                size = if (total > 0) total else it.size,
                                speed = if (speed > 0) speed else it.speed,
                            )
                        }
                        if (now - lastNotifyAt >= 500) {
                            lastNotifyAt = now
                            publishProgress(ctx, task.id)
                        }
                    },
                    onCall = { call -> calls[task.id] = call },
                    sink = out,
                )
            }
            val localUri = publish(ctx, temp, task.fileName)
            update(task.id) {
                it.copy(
                    state = TransferState.Completed,
                    localUri = localUri,
                    bytes = temp.length(),
                    speed = 0.0,
                    error = null,
                )
            }
            val done = taskById(task.id)
            if (done != null) {
                NotificationHelper.notify(
                    ctx,
                    NotificationHelper.transferNotificationId(done),
                    NotificationHelper.transferDone(ctx, done),
                )
            }
            temp.delete()
            saveHistory()
        } catch (e: Exception) {
            val current = taskById(task.id)
            if (current?.state == TransferState.Cancelled) {
                temp.delete()
                return
            }
            // 服务器忽略 Range（返回 200）时从头重下一次
            if (!restarted && offset > 0 && e is ApiException && e.message?.contains("断点") == true) {
                temp.delete()
                download(task, restarted = true)
                return
            }
            val message = when {
                e is ApiException -> e.message ?: "下载失败"
                else -> e.message ?: "下载失败"
            }
            fail(task, message)
        } finally {
            calls.remove(task.id)
        }
    }

    private fun publishProgress(context: Context, taskId: String) {
        val task = taskById(taskId) ?: return
        if (task.state != TransferState.Downloading) return
        NotificationHelper.notify(
            context,
            NotificationHelper.transferNotificationId(task),
            NotificationHelper.transferProgress(context, task),
        )
    }

    private fun fail(task: DownloadTask, message: String) {
        update(task.id) { it.copy(state = TransferState.Failed, speed = 0.0, error = message) }
        val failed = taskById(task.id) ?: return
        NotificationHelper.notify(
            ApplicationHolder.app,
            NotificationHelper.transferNotificationId(failed),
            NotificationHelper.transferDone(ApplicationHolder.app, failed),
        )
        saveHistory()
    }

    // ───────────────────────── 落盘 ─────────────────────────

    private fun publish(context: Context, temp: File, rawName: String): String? {
        val name = sanitizeName(rawName)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, name)
                put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/图吧工具箱")
                put(MediaStore.Downloads.IS_PENDING, 1)
                mimeFor(name)?.let { put(MediaStore.Downloads.MIME_TYPE, it) }
            }
            val resolver = context.contentResolver
            val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                ?: throw ApiException("无法创建下载文件")
            try {
                resolver.openOutputStream(uri)?.use { out ->
                    temp.inputStream().use { it.copyTo(out, 64 * 1024) }
                } ?: throw ApiException("无法写入下载文件")
                resolver.update(
                    uri,
                    ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) },
                    null,
                    null,
                )
                return uri.toString()
            } catch (e: Exception) {
                runCatching { resolver.delete(uri, null, null) }
                throw e
            }
        }

        if (ContextCompat.checkSelfPermission(context, Manifest.permission.WRITE_EXTERNAL_STORAGE)
            != PackageManager.PERMISSION_GRANTED
        ) {
            throw ApiException("需要存储权限才能保存到下载目录")
        }
        val dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "图吧工具箱")
            .apply { mkdirs() }
        val dest = uniqueFile(dir, name)
        temp.inputStream().use { input -> dest.outputStream().use { input.copyTo(it, 64 * 1024) } }
        return null
    }

    private fun sanitizeName(name: String): String {
        val cleaned = name.replace(Regex("[\\\\/:*?\"<>|]"), "_").trim()
        return cleaned.ifBlank { "file" }
    }

    private fun uniqueFile(dir: File, name: String): File {
        var candidate = File(dir, name)
        if (!candidate.exists()) return candidate
        val base = name.substringBeforeLast('.', name)
        val ext = name.substringAfterLast('.', "")
        var index = 1
        while (candidate.exists()) {
            val suffix = if (ext.isBlank()) "" else ".$ext"
            candidate = File(dir, "$base ($index)$suffix")
            index++
        }
        return candidate
    }

    private fun mimeFor(name: String): String? =
        MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase())

    // ───────────────────────── 状态 / 持久化 ─────────────────────────

    @Synchronized
    private fun ensureHistory() {
        if (historyLoaded) return
        historyLoaded = true
        val raw = runCatching { prefs().getString(KEY_HISTORY, null) }.getOrNull() ?: return
        val history = runCatching { json.decodeFromString<List<DownloadTask>>(raw) }.getOrDefault(emptyList())
        synchronized(lock) {
            val existing = _tasks.value.map { it.id }.toSet()
            val merged = (_tasks.value + history
                .filter { it.id !in existing }
                .map {
                    if (it.state == TransferState.Queued || it.state == TransferState.Downloading) {
                        it.copy(state = TransferState.Failed, error = "上次未完成，可重试")
                    } else it
                })
                .sortedByDescending { it.createdAt }
                .take(MAX_TASKS)
            _tasks.value = merged
        }
    }

    private fun saveHistory() {
        val history = _tasks.value
            .filter { it.state != TransferState.Queued && it.state != TransferState.Downloading }
            .take(50)
        runCatching {
            prefs().edit().putString(KEY_HISTORY, json.encodeToString(history)).apply()
        }
    }

    private fun update(id: String, transform: (DownloadTask) -> DownloadTask) {
        synchronized(lock) {
            _tasks.value = _tasks.value.map { if (it.id == id) transform(it) else it }
        }
    }

    private fun taskById(id: String): DownloadTask? = _tasks.value.firstOrNull { it.id == id }

    fun humanSize(n: Long): String = when {
        n >= 1L shl 30 -> "%.2f GB".format(n / (1L shl 30).toDouble())
        n >= 1L shl 20 -> "%.1f MB".format(n / (1L shl 20).toDouble())
        n >= 1L shl 10 -> "%.1f KB".format(n / 1024.0)
        else -> "$n B"
    }

    fun speedText(bps: Double): String = when {
        bps >= 1L shl 20 -> "%.1f MB/s".format(bps / (1L shl 20))
        bps >= 1L shl 10 -> "%.1f KB/s".format(bps / 1024.0)
        else -> "%.0f B/s".format(bps)
    }

    private fun findPc(pcId: String): SavedPc? {
        val list = PcStore.decode(runCatching { prefs().getString("pcs", null) }.getOrNull())
        if (pcId.isNotBlank()) list.firstOrNull { it.id == pcId }?.let { return it }
        val lastId = runCatching { prefs().getString("lastPcId", null) }.getOrNull()
        return list.firstOrNull { it.id == lastId } ?: list.firstOrNull()
    }
}
