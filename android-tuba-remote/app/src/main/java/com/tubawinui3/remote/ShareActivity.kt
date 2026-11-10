package com.tubawinui3.remote

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.lifecycleScope
import com.tubawinui3.remote.data.PcStore
import com.tubawinui3.remote.data.SavedPc
import com.tubawinui3.remote.service.LinkService
import com.tubawinui3.remote.ui.TubaRemoteTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import java.util.UUID

/** 分享菜单的目标页：选电脑 → 复制到缓存 → 交给 LinkService 上传 → 退出。 */
private data class ShareFile(val uri: Uri, val name: String, val size: Long, val mime: String)

private data class SharePayload(val text: String?, val files: List<ShareFile>)

class ShareActivity : ComponentActivity() {
    private val payloadState = mutableStateOf(SharePayload(null, emptyList()))
    private val copying = mutableStateOf(false)
    private val progress = mutableStateOf(-1)
    private val error = mutableStateOf<String?>(null)
    private val selectedPcId = mutableStateOf<String?>(null)
    private var pcs: List<SavedPc> = emptyList()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pcs = loadPcs()
        selectedPcId.value = pcs.firstOrNull()?.id
        payloadState.value = parseIntent(intent)
        setContent {
            TubaRemoteTheme {
                ShareSheet(
                    payload = payloadState.value,
                    pcs = pcs,
                    selectedPcId = selectedPcId.value,
                    copying = copying.value,
                    progress = progress.value,
                    error = error.value,
                    onSelectPc = { selectedPcId.value = it },
                    onSend = { pcs.firstOrNull { it.id == selectedPcId.value }?.let { send(it) } },
                    onClose = { finish() },
                    onOpenApp = {
                        startActivity(Intent(this@ShareActivity, MainActivity::class.java))
                        finish()
                    },
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        payloadState.value = parseIntent(intent)
        error.value = null
        progress.value = -1
    }

    // ───────────────────────── 意图解析 ─────────────────────────

    private fun parseIntent(intent: Intent?): SharePayload {
        if (intent == null) return SharePayload(null, emptyList())
        val text = intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()?.takeIf { it.isNotBlank() }
        val uris = mutableListOf<Uri>()
        @Suppress("DEPRECATION")
        when (intent.action) {
            Intent.ACTION_SEND -> (intent.getParcelableExtra(Intent.EXTRA_STREAM) as? Uri)?.let { uris.add(it) }
            Intent.ACTION_SEND_MULTIPLE -> {
                @Suppress("DEPRECATION")
                intent.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)?.let { uris.addAll(it) }
            }
        }
        if (uris.isEmpty()) {
            val clip = intent.clipData
            if (clip != null) {
                for (i in 0 until clip.itemCount) clip.getItemAt(i).uri?.let { uris.add(it) }
            }
        }
        val files = uris.distinct().map { uri ->
            val (name, size) = queryNameSize(uri)
            ShareFile(uri, name, size, contentResolver.getType(uri) ?: intent.type ?: "*/*")
        }
        return SharePayload(text, files)
    }

    private fun queryNameSize(uri: Uri): Pair<String, Long> {
        var name = "file"
        var size = -1L
        runCatching {
            contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
                if (c.moveToFirst()) {
                    c.getString(0)?.let { name = it }
                    if (!c.isNull(1)) size = c.getLong(1)
                }
            }
        }
        return name to size
    }

    private fun loadPcs(): List<SavedPc> {
        val prefs = getSharedPreferences("remote", 0)
        val list = PcStore.decode(prefs.getString("pcs", null))
        val last = prefs.getString("lastPcId", null)
        return list.sortedByDescending { it.id == last }
    }

    // ───────────────────────── 发送 ─────────────────────────

    private fun send(pc: SavedPc) {
        val payload = payloadState.value
        if (payload.files.isEmpty()) {
            val text = payload.text
            if (text.isNullOrBlank()) return
            LinkService.share(this, pc.id, text, emptyList(), emptyList(), emptyList())
            Toast.makeText(this, "已加入发送队列", Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        val total = payload.files.map { it.size.coerceAtLeast(0) }.sum()
        if (total > MAX_COPY_BYTES) {
            error.value = "文件总大小超过 2GB，请改用 App 内的「传输助手」发送"
            return
        }

        copying.value = true
        progress.value = if (total > 0) 0 else -1
        error.value = null
        lifecycleScope.launch {
            val dir = File(cacheDir, "share/" + UUID.randomUUID().toString().replace("-", "")).apply { mkdirs() }
            val paths = mutableListOf<String>()
            val names = mutableListOf<String>()
            val mimes = mutableListOf<String>()
            try {
                withContext(Dispatchers.IO) {
                    var copied = 0L
                    var lastUpdate = 0L
                    payload.files.forEach { file ->
                        val dest = uniqueFile(dir, sanitize(file.name))
                        contentResolver.openInputStream(file.uri)?.use { input ->
                            dest.outputStream().use { out ->
                                val buffer = ByteArray(64 * 1024)
                                while (true) {
                                    val n = input.read(buffer)
                                    if (n < 0) break
                                    out.write(buffer, 0, n)
                                    copied += n
                                    val now = System.currentTimeMillis()
                                    if (total > 0 && now - lastUpdate >= 100) {
                                        lastUpdate = now
                                        val percent = (copied * 100 / total).toInt().coerceIn(0, 100)
                                        withContext(Dispatchers.Main) { progress.value = percent }
                                    }
                                }
                            }
                        } ?: throw IOException("无法读取 ${file.name}")
                        paths.add(dest.absolutePath)
                        names.add(file.name)
                        mimes.add(file.mime)
                    }
                }
                withContext(Dispatchers.Main) {
                    LinkService.share(this@ShareActivity, pc.id, payload.text, paths, names, mimes)
                    Toast.makeText(this@ShareActivity, "已加入发送队列", Toast.LENGTH_SHORT).show()
                    finish()
                }
            } catch (e: Exception) {
                dir.deleteRecursively()
                withContext(Dispatchers.Main) {
                    copying.value = false
                    progress.value = -1
                    error.value = "读取分享内容失败：${e.message ?: "未知错误"}"
                }
            }
        }
    }

    private fun sanitize(name: String): String {
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

    companion object {
        private const val MAX_COPY_BYTES = 2L * 1024 * 1024 * 1024
    }
}

@Composable
private fun ShareSheet(
    payload: SharePayload,
    pcs: List<SavedPc>,
    selectedPcId: String?,
    copying: Boolean,
    progress: Int,
    error: String?,
    onSelectPc: (String) -> Unit,
    onSend: () -> Unit,
    onClose: () -> Unit,
    onOpenApp: () -> Unit,
) {
    val selectedPc = pcs.firstOrNull { it.id == selectedPcId } ?: pcs.firstOrNull()
    Box(
        Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.35f)),
        contentAlignment = Alignment.Center,
    ) {
        Card(Modifier.padding(20.dp).fillMaxWidth()) {
            Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("发送到电脑", style = MaterialTheme.typography.titleMedium)

                payload.text?.let { text ->
                    OutlinedTextField(
                        value = text,
                        onValueChange = {},
                        readOnly = true,
                        maxLines = 5,
                        modifier = Modifier.fillMaxWidth(),
                        label = { Text("文字") },
                    )
                }
                if (payload.files.isNotEmpty()) {
                    Text(
                        "共 ${payload.files.size} 个文件",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    payload.files.take(5).forEach { f ->
                        Text(
                            f.name + if (f.size > 0) " · ${formatSize(f.size)}" else "",
                            style = MaterialTheme.typography.bodySmall,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                    if (payload.files.size > 5) {
                        Text("…等 ${payload.files.size} 个文件", style = MaterialTheme.typography.bodySmall)
                    }
                }

                if (pcs.isEmpty()) {
                    Text(
                        "还没有连接过电脑。请先在 App 中连接电脑，再回来分享。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton(onClick = onClose) { Text("取消") }
                        Button(onClick = onOpenApp) { Text("打开 App") }
                    }
                    return@Column
                }

                var expanded by remember { mutableStateOf(false) }
                Box {
                    OutlinedButton(onClick = { expanded = true }, enabled = !copying) {
                        Text("发送到 " + (selectedPc?.name?.ifBlank { selectedPc.host } ?: "选择电脑"))
                    }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        pcs.forEach { pc ->
                            DropdownMenuItem(
                                text = { Text(pc.name.ifBlank { pc.host }) },
                                onClick = {
                                    onSelectPc(pc.id)
                                    expanded = false
                                },
                            )
                        }
                    }
                }

                error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }

                if (copying) {
                    if (progress >= 0) {
                        LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth())
                    } else {
                        LinearProgressIndicator(Modifier.fillMaxWidth())
                    }
                    Text("正在准备文件…", style = MaterialTheme.typography.bodySmall)
                }

                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    TextButton(onClick = onClose, enabled = !copying) { Text("取消") }
                    Button(onClick = onSend, enabled = !copying && selectedPc != null) { Text("发送") }
                }
            }
        }
    }
}

private fun formatSize(n: Long): String = when {
    n >= 1L shl 30 -> "%.2f GB".format(n / (1L shl 30).toDouble())
    n >= 1L shl 20 -> "%.1f MB".format(n / (1L shl 20).toDouble())
    n >= 1L shl 10 -> "%.1f KB".format(n / 1024.0)
    else -> "$n B"
}
