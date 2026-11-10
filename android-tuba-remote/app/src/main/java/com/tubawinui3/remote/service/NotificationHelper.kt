package com.tubawinui3.remote.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.webkit.MimeTypeMap
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.tubawinui3.remote.MainActivity
import com.tubawinui3.remote.R
import com.tubawinui3.remote.data.ChatMessage
import com.tubawinui3.remote.data.DownloadTask
import com.tubawinui3.remote.data.TransferState

/**
 * 手机端通知：常驻连接、电脑消息、传输进度/完成、连接异常。
 * 所有通知都固定使用一个单色小图标（系统会着色）。
 */
object NotificationHelper {
    const val CHANNEL_LINK = "link"
    const val CHANNEL_MESSAGES = "messages"
    const val CHANNEL_TRANSFER = "transfer"
    const val CHANNEL_ALERTS = "alerts"

    const val NOTIF_LINK = 1001
    const val NOTIF_SHARE = 4100

    private const val NOTIF_MESSAGE_BASE = 200_000
    private const val NOTIF_TRANSFER_BASE = 300_000
    private const val NOTIF_ALERT = 400_000

    fun messageNotificationId(message: ChatMessage): Int =
        NOTIF_MESSAGE_BASE + (message.seq % 10_000).toInt()

    fun transferNotificationId(task: DownloadTask): Int =
        NOTIF_TRANSFER_BASE + (task.id.hashCode() and 0x3FF)

    fun ensureChannels(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_LINK, "电脑连接", NotificationManager.IMPORTANCE_LOW).apply {
                description = "保持与电脑的连接，接收电脑发来的文件"
                setShowBadge(false)
            }
        )
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_MESSAGES, "电脑消息", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "电脑发来的文字、图片和文件"
                enableVibration(true)
            }
        )
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_TRANSFER, "文件传输", NotificationManager.IMPORTANCE_LOW).apply {
                description = "文件下载进度与完成提示"
                setShowBadge(false)
            }
        )
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL_ALERTS, "连接提醒", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "连接失效、发送失败等提醒"
            }
        )
    }

    private fun base(context: Context, channel: String): NotificationCompat.Builder =
        NotificationCompat.Builder(context, channel)
            .setSmallIcon(R.drawable.ic_notification)
            .setColor(0xFF0F6CBD.toInt())

    fun linkNotification(context: Context, pcName: String): Notification =
        base(context, CHANNEL_LINK)
            .setContentTitle("已连接 ${pcName.ifBlank { "电脑" }}")
            .setContentText("电脑发来的文件和消息会在后台接收")
            .setContentIntent(openApp(context, tool = "CHAT", requestCode = 1))
            .addAction(0, "断开", serviceAction(context, LinkService.ACTION_STOP, 2))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .build()

    fun messageText(context: Context, pcName: String, message: ChatMessage): Notification {
        val text = message.text.ifBlank { "（空消息）" }
        return base(context, CHANNEL_MESSAGES)
            .setContentTitle("电脑消息" + if (pcName.isBlank()) "" else " · $pcName")
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openApp(context, tool = "CHAT", requestCode = 3))
            .addAction(
                0, "复制",
                PendingIntent.getBroadcast(
                    context,
                    messageNotificationId(message),
                    Intent(context, NotificationActionReceiver::class.java)
                        .setAction(NotificationActionReceiver.ACTION_COPY_TEXT)
                        .putExtra(NotificationActionReceiver.EXTRA_TEXT, text),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
                ),
            )
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()
    }

    fun messageFile(context: Context, pcName: String, message: ChatMessage, pcId: String): Notification {
        val title = if (message.type == "image") "电脑发来图片" else "电脑发来文件"
        val size = if (message.size > 0) " · ${humanSize(message.size)}" else ""
        val id = messageNotificationId(message)
        return base(context, CHANNEL_MESSAGES)
            .setContentTitle(title + if (pcName.isBlank()) "" else " · $pcName")
            .setContentText(message.fileName + size)
            .setContentIntent(openApp(context, tool = "CHAT", requestCode = 4))
            .addAction(
                0, "下载",
                PendingIntent.getForegroundService(
                    context,
                    id + 1,
                    Intent(context, LinkService::class.java)
                        .setAction(LinkService.ACTION_DOWNLOAD)
                        .putExtra(LinkService.EXTRA_MESSAGE_ID, message.id)
                        .putExtra(LinkService.EXTRA_FILE_NAME, message.fileName)
                        .putExtra(LinkService.EXTRA_FILE_SIZE, message.size)
                        .putExtra(LinkService.EXTRA_PC_ID, pcId),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
                ),
            )
            .addAction(
                0, "忽略",
                PendingIntent.getBroadcast(
                    context,
                    id + 2,
                    Intent(context, NotificationActionReceiver::class.java)
                        .setAction(NotificationActionReceiver.ACTION_DISMISS)
                        .putExtra(NotificationActionReceiver.EXTRA_NOTIFICATION_ID, id),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
                ),
            )
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()
    }

    fun transferProgress(context: Context, task: DownloadTask): Notification {
        val percent = if (task.size > 0) ((task.bytes * 100) / task.size).toInt().coerceIn(0, 100) else 0
        val detail = when {
            task.size > 0 -> "${humanSize(task.bytes)} / ${humanSize(task.size)}" +
                if (task.speed > 0) " · ${speedText(task.speed)}" else ""
            task.bytes > 0 -> humanSize(task.bytes)
            else -> "准备下载…"
        }
        val builder = base(context, CHANNEL_TRANSFER)
            .setContentTitle("正在下载 ${task.fileName}")
            .setContentText(detail)
            .setContentIntent(openApp(context, tool = "DOWNLOADS", requestCode = 5))
            .addAction(
                0, "取消",
                PendingIntent.getForegroundService(
                    context,
                    transferNotificationId(task),
                    Intent(context, LinkService::class.java)
                        .setAction(LinkService.ACTION_CANCEL_DOWNLOAD)
                        .putExtra(LinkService.EXTRA_TASK_ID, task.id)
                        .putExtra(LinkService.EXTRA_PC_ID, task.pcId),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
                ),
            )
            .setOngoing(true)
            .setOnlyAlertOnce(true)
        if (task.size > 0) builder.setProgress(100, percent, false) else builder.setProgress(0, 0, true)
        return builder.build()
    }

    fun transferDone(context: Context, task: DownloadTask): Notification {
        val ok = task.state == TransferState.Completed
        val builder = base(context, if (ok) CHANNEL_TRANSFER else CHANNEL_ALERTS)
            .setContentTitle(if (ok) "下载完成" else "下载失败")
            .setContentText(
                when {
                    ok -> task.fileName + if (task.size > 0) " · ${humanSize(task.size)}" else ""
                    else -> "${task.fileName}：${task.error ?: "下载中断"}"
                }
            )
            .setContentIntent(openApp(context, tool = "DOWNLOADS", requestCode = 6))
            .setAutoCancel(true)
        if (ok && task.localUri != null) {
            openFileIntent(context, task)?.let { builder.addAction(0, "打开", it) }
        } else if (!ok) {
            builder.addAction(
                0, "重试",
                PendingIntent.getForegroundService(
                    context,
                    transferNotificationId(task) + 7,
                    Intent(context, LinkService::class.java)
                        .setAction(LinkService.ACTION_RETRY_DOWNLOAD)
                        .putExtra(LinkService.EXTRA_TASK_ID, task.id)
                        .putExtra(LinkService.EXTRA_PC_ID, task.pcId),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
                ),
            )
        }
        return builder.build()
    }

    fun shareProgress(context: Context, name: String, sent: Long, total: Long): Notification {
        val builder = base(context, CHANNEL_TRANSFER)
            .setContentTitle("发送到电脑")
            .setContentText(if (total > 0) "$name · ${humanSize(sent)} / ${humanSize(total)}" else "$name · ${humanSize(sent)}")
            .setOngoing(true)
            .setOnlyAlertOnce(true)
        if (total > 0) builder.setProgress(100, ((sent * 100) / total).toInt().coerceIn(0, 100), false)
        else builder.setProgress(0, 0, true)
        return builder.build()
    }

    fun shareResult(context: Context, name: String, ok: Boolean, detail: String? = null): Notification =
        base(context, if (ok) CHANNEL_TRANSFER else CHANNEL_ALERTS)
            .setContentTitle(if (ok) "已发送到电脑" else "发送失败")
            .setContentText(if (ok) name else "$name：${detail ?: "网络错误"}")
            .setAutoCancel(true)
            .build()

    fun alert(context: Context, title: String, text: String): Notification =
        base(context, CHANNEL_ALERTS)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openApp(context, tool = null, requestCode = 7))
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

    fun notify(context: Context, id: Int, notification: Notification) {
        try {
            NotificationManagerCompat.from(context).notify(id, notification)
        } catch (_: SecurityException) {
            // Android 13+ 未授予通知权限：服务仍可运行，只是不显示通知
        }
    }

    fun notifyAlert(context: Context, title: String, text: String) =
        notify(context, NOTIF_ALERT, alert(context, title, text))

    fun cancel(context: Context, id: Int) {
        runCatching { NotificationManagerCompat.from(context).cancel(id) }
    }

    private fun serviceAction(context: Context, action: String, requestCode: Int): PendingIntent =
        PendingIntent.getService(
            context,
            requestCode,
            Intent(context, LinkService::class.java).setAction(action),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

    private fun openApp(context: Context, tool: String?, requestCode: Int): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            if (!tool.isNullOrBlank()) putExtra("open", tool)
        }
        return PendingIntent.getActivity(
            context, requestCode, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun openFileIntent(context: Context, task: DownloadTask): PendingIntent? {
        val uri = task.localUri?.let(Uri::parse) ?: return null
        val ext = task.fileName.substringAfterLast('.', "").lowercase()
        val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "*/*"
        val intent = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, mime)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        return PendingIntent.getActivity(
            context,
            transferNotificationId(task) + 8,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    fun humanSize(n: Long): String = when {
        n >= 1L shl 30 -> "%.2f GB".format(n / (1L shl 30).toDouble())
        n >= 1L shl 20 -> "%.1f MB".format(n / (1L shl 20).toDouble())
        n >= 1L shl 10 -> "%.1f KB".format(n / 1024.0)
        else -> "$n B"
    }

    private fun speedText(bps: Double): String = when {
        bps >= 1L shl 20 -> "%.1f MB/s".format(bps / (1L shl 20))
        bps >= 1L shl 10 -> "%.1f KB/s".format(bps / 1024.0)
        else -> "%.0f B/s".format(bps)
    }
}
