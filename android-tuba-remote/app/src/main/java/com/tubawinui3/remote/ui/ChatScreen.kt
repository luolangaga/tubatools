package com.tubawinui3.remote.ui

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.RemoteViewModel
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.ChatMessage
import com.tubawinui3.remote.data.DownloadQueue
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.text.DateFormat
import java.util.Date
import java.util.concurrent.atomic.AtomicLong

private const val MAX_PREVIEW_BYTES = 12L * 1024 * 1024

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ChatScreen(
    vm: RemoteViewModel,
    client: ApiClient,
    onBack: () -> Unit,
    onOpenDownloads: () -> Unit,
) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var text by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var speedText by remember { mutableStateOf("") }
    val listState = rememberLazyListState()

    fun toast(s: String) = Toast.makeText(ctx, s, Toast.LENGTH_SHORT).show()

    fun upload(uri: Uri, isImage: Boolean) {
        val (name, size) = queryNameSize(ctx, uri)
        busy = true
        speedText = ""
        scope.launch {
            val counter = AtomicLong(0)
            val ticker = launch {
                var last = 0L
                var lastAt = System.currentTimeMillis()
                while (true) {
                    delay(300)
                    val now = counter.get()
                    val at = System.currentTimeMillis()
                    val dt = at - lastAt
                    if (dt > 0) speedText = fmtSpeed((now - last) * 1000.0 / dt)
                    last = now
                    lastAt = at
                }
            }
            try {
                val m = client.chatUpload(name, isImage, size) {
                    CountingInputStream(ctx.contentResolver.openInputStream(uri) ?: throw ApiException("无法读取所选文件"), counter)
                }
                vm.addMessage(m)
            } catch (e: ApiException) {
                toast("发送失败：${e.message}")
            } finally {
                ticker.cancel()
                busy = false
                speedText = ""
            }
        }
    }

    val pickImage = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { it?.let { u -> upload(u, true) } }
    val pickFile = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { it?.let { u -> upload(u, false) } }

    LaunchedEffect(vm.messages.size) {
        if (vm.messages.isNotEmpty()) listState.animateScrollToItem(vm.messages.size - 1)
    }

    Scaffold(
        modifier = Modifier.imePadding(),
        topBar = {
            TopAppBar(
                title = { Text("传输助手") },
                navigationIcon = { TextButton(onClick = onBack) { Text("返回") } },
                actions = { TextButton(onClick = onOpenDownloads) { Text("传输队列") } },
            )
        },
        bottomBar = {
            Column(Modifier.padding(8.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    TextButton(enabled = !busy, onClick = { pickImage.launch("image/*") }) { Text("图片") }
                    TextButton(enabled = !busy, onClick = { pickFile.launch("*/*") }) { Text("文件") }
                    if (busy) Text(if (speedText.isNotEmpty()) "发送中 $speedText" else "发送中…", Modifier.align(Alignment.CenterVertically), style = MaterialTheme.typography.bodySmall)
                }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(value = text, onValueChange = { text = it }, modifier = Modifier.weight(1f), maxLines = 4, placeholder = { Text("发送给电脑…") })
                    Button(enabled = text.isNotBlank(), onClick = {
                        val t = text.trim()
                        text = ""
                        scope.launch {
                            try { vm.addMessage(client.chatSendText(t)) } catch (e: ApiException) { toast("发送失败：${e.message}"); text = t }
                        }
                    }) { Text("发送") }
                }
            }
        },
    ) { pad ->
        LazyColumn(
            Modifier.fillMaxSize().padding(pad).padding(horizontal = 12.dp),
            state = listState, verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            items(vm.messages, key = { it.seq }) { m ->
                Bubble(m, client, onSave = {
                    DownloadQueue.enqueue(m, vm.currentPcId ?: "")
                    toast("已加入下载队列")
                })
            }
        }
    }
}

@Composable
private fun Bubble(m: ChatMessage, client: ApiClient, onSave: () -> Unit) {
    val mine = m.from == "phone"
    Row(Modifier.fillMaxWidth(), horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start) {
        Card(
            Modifier.widthIn(max = 300.dp),
            colors = CardDefaults.cardColors(
                containerColor = if (mine) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant,
            ),
        ) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                when (m.type) {
                    "text" -> Text(m.text)
                    else -> {
                        if (m.type == "image" && m.size in 1..MAX_PREVIEW_BYTES) {
                            val bmp by produceState<ImageBitmap?>(null, m.id) { value = loadImage(client, m) }
                            bmp?.let { Image(it, contentDescription = m.fileName, modifier = Modifier.fillMaxWidth()) }
                        }
                        Text("${if (m.type == "image") "🖼 " else "📄 "}${m.fileName}", style = MaterialTheme.typography.bodyMedium)
                        Text(humanSize(m.size), style = MaterialTheme.typography.bodySmall)
                        TextButton(onClick = onSave) { Text(if (mine) "再次保存" else "保存到手机") }
                    }
                }
                Text(
                    DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(m.time)),
                    style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private suspend fun loadImage(client: ApiClient, m: ChatMessage): ImageBitmap? = try {
    val out = ByteArrayOutputStream()
    client.chatDownload(m.id, out)
    val bytes = out.toByteArray()
    withContext(Dispatchers.Default) {
        android.graphics.BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
    }
} catch (_: Exception) {
    null
}

/** 统计已读取字节数的输入流（用于显示上传速度）。 */
private class CountingInputStream(private val inner: InputStream, private val counter: AtomicLong) : InputStream() {
    override fun read(): Int = inner.read().also { if (it >= 0) counter.incrementAndGet() }
    override fun read(b: ByteArray, off: Int, len: Int): Int =
        inner.read(b, off, len).also { if (it > 0) counter.addAndGet(it.toLong()) }

    override fun close() = inner.close()
    override fun available(): Int = inner.available()
}

private fun fmtSpeed(bps: Double): String = when {
    bps >= 1L shl 30 -> "%.2f GB/s".format(bps / (1L shl 30))
    bps >= 1L shl 20 -> "%.1f MB/s".format(bps / (1L shl 20))
    bps >= 1L shl 10 -> "%.1f KB/s".format(bps / 1024.0)
    else -> "%.0f B/s".format(bps)
}

private fun humanSize(n: Long): String = when {
    n >= 1L shl 30 -> "%.2f GB".format(n / (1L shl 30).toDouble())
    n >= 1L shl 20 -> "%.1f MB".format(n / (1L shl 20).toDouble())
    n >= 1L shl 10 -> "%.1f KB".format(n / 1024.0)
    else -> "$n B"
}

private fun queryNameSize(ctx: Context, uri: Uri): Pair<String, Long> {
    var name = "file"
    var size = -1L
    ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
        if (c.moveToFirst()) {
            c.getString(0)?.let { name = it }
            if (!c.isNull(1)) size = c.getLong(1)
        }
    }
    return name to size
}
