package com.tubawinui3.remote.service

import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.widget.Toast
import androidx.core.app.NotificationManagerCompat

/**
 * 通知按钮的轻量动作：复制电脑消息、忽略文件通知。
 * 剪贴板“写入”在后台是允许的（Android 10+ 限制的是读取）。
 */
class NotificationActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_COPY_TEXT -> {
                val text = intent.getStringExtra(EXTRA_TEXT).orEmpty()
                if (text.isBlank()) return
                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(ClipData.newPlainText("电脑消息", text))
                Toast.makeText(context, "已复制", Toast.LENGTH_SHORT).show()
            }
            ACTION_DISMISS -> {
                val id = intent.getIntExtra(EXTRA_NOTIFICATION_ID, 0)
                if (id != 0) runCatching { NotificationManagerCompat.from(context).cancel(id) }
            }
        }
    }

    companion object {
        const val ACTION_COPY_TEXT = "com.tubawinui3.remote.COPY_TEXT"
        const val ACTION_DISMISS = "com.tubawinui3.remote.DISMISS"
        const val EXTRA_TEXT = "text"
        const val EXTRA_NOTIFICATION_ID = "notification_id"
    }
}
