package com.tubawinui3.remote.service

import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.tubawinui3.remote.AppVisibility
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.ChatMessage
import com.tubawinui3.remote.data.DownloadQueue
import com.tubawinui3.remote.data.PcStore
import com.tubawinui3.remote.data.SavedPc
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.File

/**
 * 连接电脑的前台服务（connectedDevice）：
 * - 后台轮询聊天，电脑发来消息/文件时弹通知；
 * - 接收分享菜单上传任务；
 * - 托管下载队列（下载本身在 DownloadQueue 里执行）。
 *
 * 必须在 App 可见时启动（Android 12+ 不允许后台启动前台服务）。
 */
class LinkService : Service() {
    private var scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var pollJob: Job? = null
    private var client: ApiClient? = null
    private var pc: SavedPc? = null

    private val prefs get() = getSharedPreferences("remote", 0)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> startWith(intent.getStringExtra(EXTRA_PC_ID))
            ACTION_STOP -> {
                stopCore()
                return START_NOT_STICKY
            }
            ACTION_SHARE -> handleShare(intent)
            ACTION_DOWNLOAD -> {
                if (!ensureForeground(intent.getStringExtra(EXTRA_PC_ID))) {
                    stopCore()
                    return START_NOT_STICKY
                }
                handleDownload(intent)
            }
            ACTION_CANCEL_DOWNLOAD -> {
                if (!ensureForeground(intent.getStringExtra(EXTRA_PC_ID))) {
                    stopCore()
                    return START_NOT_STICKY
                }
                DownloadQueue.cancel(intent.getStringExtra(EXTRA_TASK_ID).orEmpty())
            }
            ACTION_RETRY_DOWNLOAD -> {
                if (!ensureForeground(intent.getStringExtra(EXTRA_PC_ID))) {
                    stopCore()
                    return START_NOT_STICKY
                }
                DownloadQueue.retry(intent.getStringExtra(EXTRA_TASK_ID).orEmpty())
            }
            else -> restore()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        pollJob?.cancel()
        scope.cancel()
        client = null
        runCatching { ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE) }
        super.onDestroy()
    }

    // ───────────────────────── 启动 / 恢复 ─────────────────────────

    private fun restore() {
        if (!prefs.getBoolean(KEY_BG_ENABLED, true)) {
            ensureForeground(null)
            stopCore()
            return
        }
        val lastId = prefs.getString(KEY_LAST_PC, null)
        if (lastId.isNullOrBlank()) {
            ensureForeground(null)
            stopCore()
            return
        }
        startWith(lastId)
    }

    private fun startWith(pcId: String?) {
        val target = findPc(pcId)
        ensureForeground(pcId)
        if (target == null) {
            stopCore()
            return
        }
        startPolling(target)
    }

    /** 保证 startForeground 一定被调用（Android 12+ 允许前台启动的硬性要求）。 */
    private fun ensureForeground(pcId: String? = null): Boolean {
        val target = if (!pcId.isNullOrBlank()) findPc(pcId) else (pc ?: findPc(null))
        pc = target
        val name = target?.name?.ifBlank { target.host } ?: "电脑"
        val notification = NotificationHelper.linkNotification(this, name)
        val type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
        } else {
            0
        }
        ServiceCompat.startForeground(this, NotificationHelper.NOTIF_LINK, notification, type)
        return target != null
    }

    // ───────────────────────── 聊天轮询 ─────────────────────────

    private fun startPolling(target: SavedPc) {
        pollJob?.cancel()
        pollJob = scope.launch {
            val api = ApiClient(target.host, target.port, target.token, target.fingerprint)
            client = api
            var cursor = prefs.getLong(KEY_BG_CURSOR, 0L)
            var startSeq = cursor
            try {
                val tail = api.chatTail(1)
                if (tail != null && tail.seq > cursor) cursor = tail.seq
                startSeq = cursor
                prefs.edit().putLong(KEY_BG_CURSOR, cursor).apply()
            } catch (_: Exception) {
                // 首次连不上就按 0 开始；下面的循环会退避重试
            }

            var failures = 0
            while (isActive) {
                try {
                    val fresh = api.chatList(cursor)
                    failures = 0
                    for (message in fresh) {
                        if (message.seq <= cursor) continue
                        cursor = message.seq
                        if (message.from == "pc" && message.seq > startSeq && !AppVisibility.isForeground) {
                            notifyMessage(target, message)
                        }
                    }
                    prefs.edit().putLong(KEY_BG_CURSOR, cursor).apply()
                    delay(3_000)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: ApiException) {
                    if (e.code == 401) {
                        NotificationHelper.notifyAlert(
                            this@LinkService,
                            "连接已失效",
                            "请在 App 中重新扫描电脑上的二维码配对。",
                        )
                        stopCore()
                        return@launch
                    }
                    failures++
                    delay(backoff(failures))
                } catch (_: Exception) {
                    failures++
                    delay(backoff(failures))
                }
            }
        }
    }

    private fun backoff(failures: Int): Long =
        minOf(60_000L, 5_000L * (1L shl failures.coerceIn(0, 4)))

    private fun notifyMessage(target: SavedPc, message: ChatMessage) {
        val name = target.name.ifBlank { target.host }
        if (message.type == "text") {
            NotificationHelper.notify(
                this,
                NotificationHelper.messageNotificationId(message),
                NotificationHelper.messageText(this, name, message),
            )
        } else {
            NotificationHelper.notify(
                this,
                NotificationHelper.messageNotificationId(message),
                NotificationHelper.messageFile(this, name, message, target.id),
            )
            if (prefs.getBoolean(KEY_AUTO_DOWNLOAD, false)) {
                DownloadQueue.enqueue(message, target.id)
            }
        }
    }

    // ───────────────────────── 分享上传 ─────────────────────────

    private fun handleShare(intent: Intent) {
        val pcId = intent.getStringExtra(EXTRA_PC_ID)
        val target = findPc(pcId)
        ensureForeground(pcId)
        if (target == null) {
            NotificationHelper.notifyAlert(this, "发送失败", "电脑未连接，请先在 App 中连接电脑。")
            stopCore()
            return
        }
        val text = intent.getStringExtra(EXTRA_TEXT)
        val paths = intent.getStringArrayListExtra(EXTRA_PATHS).orEmpty()
        val names = intent.getStringArrayListExtra(EXTRA_NAMES).orEmpty()
        val mimes = intent.getStringArrayListExtra(EXTRA_MIMES).orEmpty()
        val job = scope.launch {
            val api = ApiClient(target.host, target.port, target.token, target.fingerprint)
            var sentAny = false
            try {
                if (!text.isNullOrBlank()) {
                    api.chatSendText(text)
                    sentAny = true
                }
                paths.forEachIndexed { index, path ->
                    val file = File(path)
                    if (!file.isFile) return@forEachIndexed
                    val displayName = names.getOrNull(index)?.ifBlank { file.name } ?: file.name
                    val isImage = (mimes.getOrNull(index) ?: "").startsWith("image/")
                    var lastNotifyAt = 0L
                    api.chatUpload(
                        name = displayName,
                        isImage = isImage,
                        length = file.length(),
                        onProgress = { sent ->
                            val now = System.currentTimeMillis()
                            if (now - lastNotifyAt >= 500) {
                                lastNotifyAt = now
                                NotificationHelper.notify(
                                    this@LinkService,
                                    NotificationHelper.NOTIF_SHARE,
                                    NotificationHelper.shareProgress(this@LinkService, displayName, sent, file.length()),
                                )
                            }
                        },
                        open = { file.inputStream() },
                    )
                    sentAny = true
                    NotificationHelper.notify(
                        this@LinkService,
                        NotificationHelper.NOTIF_SHARE,
                        NotificationHelper.shareResult(this@LinkService, displayName, true),
                    )
                }
                if (!sentAny) {
                    NotificationHelper.notify(
                        this@LinkService,
                        NotificationHelper.NOTIF_SHARE,
                        NotificationHelper.shareResult(this@LinkService, "分享内容", false, "没有可发送的内容"),
                    )
                }
            } catch (e: Exception) {
                NotificationHelper.notify(
                    this@LinkService,
                    NotificationHelper.NOTIF_SHARE,
                    NotificationHelper.shareResult(this@LinkService, "分享内容", false, e.message),
                )
            } finally {
                paths.forEach { path -> runCatching { File(path).parentFile?.deleteRecursively() } }
                if (pollJob?.isActive != true) {
                    stopCore()
                }
            }
        }
    }

    // ───────────────────────── 下载 ─────────────────────────

    private fun handleDownload(intent: Intent) {
        val messageId = intent.getStringExtra(EXTRA_MESSAGE_ID) ?: return
        val fileName = intent.getStringExtra(EXTRA_FILE_NAME) ?: "file"
        val size = intent.getLongExtra(EXTRA_FILE_SIZE, 0L)
        val pcId = intent.getStringExtra(EXTRA_PC_ID) ?: pc?.id ?: ""
        val type = if (isImageName(fileName)) "image" else "file"
        val message = ChatMessage(
            seq = 0,
            id = messageId,
            from = "pc",
            type = type,
            text = "",
            fileName = fileName,
            size = size,
            time = System.currentTimeMillis(),
        )
        DownloadQueue.enqueue(message, pcId)
    }

    private fun isImageName(name: String): Boolean =
        name.substringAfterLast('.', "").lowercase() in setOf("jpg", "jpeg", "png", "gif", "webp", "bmp", "heic")

    // ───────────────────────── 工具 ─────────────────────────

    private fun findPc(pcId: String?): SavedPc? {
        val list = PcStore.decode(prefs.getString("pcs", null))
        if (!pcId.isNullOrBlank()) list.firstOrNull { it.id == pcId }?.let { return it }
        val lastId = prefs.getString(KEY_LAST_PC, null)
        return list.firstOrNull { it.id == lastId } ?: list.firstOrNull()
    }

    private fun stopCore() {
        pollJob?.cancel()
        pollJob = null
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    companion object {
        const val ACTION_START = "com.tubawinui3.remote.service.START"
        const val ACTION_STOP = "com.tubawinui3.remote.service.STOP"
        const val ACTION_SHARE = "com.tubawinui3.remote.service.SHARE"
        const val ACTION_DOWNLOAD = "com.tubawinui3.remote.service.DOWNLOAD"
        const val ACTION_CANCEL_DOWNLOAD = "com.tubawinui3.remote.service.CANCEL_DOWNLOAD"
        const val ACTION_RETRY_DOWNLOAD = "com.tubawinui3.remote.service.RETRY_DOWNLOAD"

        const val EXTRA_PC_ID = "pc_id"
        const val EXTRA_TEXT = "text"
        const val EXTRA_PATHS = "paths"
        const val EXTRA_NAMES = "names"
        const val EXTRA_MIMES = "mimes"
        const val EXTRA_MESSAGE_ID = "message_id"
        const val EXTRA_FILE_NAME = "file_name"
        const val EXTRA_FILE_SIZE = "file_size"
        const val EXTRA_TASK_ID = "task_id"

        const val KEY_BG_ENABLED = "bgEnabled"
        const val KEY_AUTO_DOWNLOAD = "autoDownload"
        const val KEY_BG_CURSOR = "bgCursor"
        const val KEY_LAST_PC = "lastPcId"

        fun start(context: Context, pcId: String) {
            val intent = Intent(context, LinkService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_PC_ID, pcId)
            ContextCompat.startForegroundService(context, intent)
        }

        fun stop(context: Context) {
            runCatching { context.stopService(Intent(context, LinkService::class.java)) }
        }

        fun share(
            context: Context,
            pcId: String,
            text: String?,
            paths: List<String>,
            names: List<String>,
            mimes: List<String>,
        ) {
            val intent = Intent(context, LinkService::class.java)
                .setAction(ACTION_SHARE)
                .putExtra(EXTRA_PC_ID, pcId)
                .putExtra(EXTRA_TEXT, text)
                .putStringArrayListExtra(EXTRA_PATHS, ArrayList(paths))
                .putStringArrayListExtra(EXTRA_NAMES, ArrayList(names))
                .putStringArrayListExtra(EXTRA_MIMES, ArrayList(mimes))
            ContextCompat.startForegroundService(context, intent)
        }

        fun cancelDownload(context: Context, taskId: String, pcId: String) {
            val intent = Intent(context, LinkService::class.java)
                .setAction(ACTION_CANCEL_DOWNLOAD)
                .putExtra(EXTRA_TASK_ID, taskId)
                .putExtra(EXTRA_PC_ID, pcId)
            runCatching { context.startService(intent) }
        }
    }
}
