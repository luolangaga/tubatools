package com.tubawinui3.remote.ui

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.view.WindowManager
import android.widget.ImageView
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.CameraInfo
import com.tubawinui3.remote.data.CameraStreamSession
import com.tubawinui3.remote.data.DownloadQueue
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun CameraScreen(client: ApiClient, onBack: () -> Unit) {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var cameras by remember { mutableStateOf<List<CameraInfo>>(emptyList()) }
    var selected by remember { mutableStateOf(0) }
    var width by remember { mutableStateOf(960) }
    var fps by remember { mutableStateOf(10) }
    var quality by remember { mutableStateOf(60) }
    var reconnect by remember { mutableStateOf(0) }
    var loading by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    var connected by remember { mutableStateOf(false) }
    var fullscreen by remember { mutableStateOf(false) }
    var bitmap by remember { mutableStateOf<Bitmap?>(null) }
    var statsText by remember { mutableStateOf("") }
    val counters = remember { longArrayOf(0, 0) }

    LaunchedEffect(Unit) {
        try {
            val list = client.cameraList()
            cameras = list.cameras
            width = list.defaultWidth
            fps = list.defaultFps
            quality = list.defaultQuality
            selected = list.preferredId
                ?.let { id -> list.cameras.indexOfFirst { it.id == id } }
                ?.takeIf { it >= 0 } ?: 0
            if (list.cameras.isEmpty()) error = "电脑上没有检测到摄像头"
        } catch (e: ApiException) {
            error = e.message
        } finally {
            loading = false
        }
    }

    val camera = cameras.getOrNull(selected)
    val session = remember(camera?.index, width, fps, quality, reconnect) {
        camera?.let { CameraStreamSession(client, it.index, width, fps, quality) }
    }

    LaunchedEffect(session) {
        val activeSession = session ?: return@LaunchedEffect
        error = null
        connected = false
        activeSession.start(this) { message ->
            error = message
            connected = false
        }
        val decoder = CameraFrameDecoder()
        for (bytes in activeSession.frames) {
            if (!isActive) break
            val decoded = withContext(Dispatchers.Default) { decoder.decode(bytes) } ?: continue
            bitmap = decoded
            connected = true
            counters[0]++
            counters[1] += bytes.size
        }
        // 电脑端单个请求 15 分钟硬上限，或网络抖动导致流结束：自动重连
        if (isActive) {
            delay(3_000)
            reconnect++
        }
    }

    DisposableEffect(session) {
        onDispose { session?.stop() }
    }

    LaunchedEffect(Unit) {
        while (true) {
            delay(1000)
            val frames = counters[0]
            val bytes = counters[1]
            counters[0] = 0
            counters[1] = 0
            statsText = when {
                frames > 0 -> "$frames fps · ${DownloadQueue.humanSize(bytes)}/s"
                connected -> "等待画面…"
                else -> ""
            }
        }
    }

    // 全屏：隐藏系统栏 + 常亮，退出时恢复
    val view = LocalView.current
    val activity = ctx as? Activity
    DisposableEffect(fullscreen) {
        val window = activity?.window
        if (window != null) {
            val controller = WindowCompat.getInsetsController(window, view)
            if (fullscreen) {
                WindowCompat.setDecorFitsSystemWindows(window, false)
                controller.hide(WindowInsetsCompat.Type.systemBars())
                controller.systemBarsBehavior =
                    WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
                window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            } else {
                controller.show(WindowInsetsCompat.Type.systemBars())
                WindowCompat.setDecorFitsSystemWindows(window, true)
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
        }
        onDispose {
            if (window != null) {
                WindowCompat.getInsetsController(window, view).show(WindowInsetsCompat.Type.systemBars())
                WindowCompat.setDecorFitsSystemWindows(window, true)
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
        }
    }

    BackHandler(enabled = fullscreen) { fullscreen = false }

    fun snapshot() {
        val index = camera?.index ?: return
        scope.launch {
            try {
                val bytes = client.cameraSnapshot(index, width, quality)
                val name = "摄像头_" + SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date()) + ".jpg"
                DownloadQueue.saveBytes(ctx, name, bytes)
                Toast.makeText(ctx, "已保存：$name", Toast.LENGTH_LONG).show()
            } catch (e: Exception) {
                Toast.makeText(ctx, "截图失败：${e.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    Scaffold(
        topBar = {
            if (!fullscreen) {
                TopAppBar(
                    title = { Text("摄像头直播") },
                    navigationIcon = { TextButton(onClick = onBack) { Text("返回") } },
                    actions = { TextButton(onClick = { fullscreen = true }) { Text("全屏") } },
                )
            }
        },
    ) { pad ->
        Column(
            Modifier.fillMaxSize().padding(if (fullscreen) PaddingValues(0.dp) else pad),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (!fullscreen) {
                Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    if (loading) {
                        LinearProgressIndicator(Modifier.fillMaxWidth())
                        Text("正在读取摄像头列表…", style = MaterialTheme.typography.bodySmall)
                    }
                    error?.let {
                        Card(Modifier.fillMaxWidth()) {
                            Text(
                                it,
                                Modifier.padding(12.dp),
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                    }
                    if (cameras.isNotEmpty()) {
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            cameras.forEachIndexed { index, item ->
                                FilterChip(
                                    selected = index == selected,
                                    onClick = {
                                        selected = index
                                        connected = false
                                        bitmap = null
                                    },
                                    label = { Text(item.name) },
                                )
                            }
                        }
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text("清晰度", style = MaterialTheme.typography.bodySmall)
                            listOf(480 to "480p", 720 to "720p", 1080 to "1080p").forEach { (w, label) ->
                                FilterChip(
                                    selected = width == when (w) { 480 -> 854; 720 -> 1280; else -> 1920 },
                                    onClick = { width = when (w) { 480 -> 854; 720 -> 1280; else -> 1920 } },
                                    label = { Text(label) },
                                )
                            }
                        }
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text("帧率", style = MaterialTheme.typography.bodySmall)
                            listOf(5, 10, 15).forEach { value ->
                                FilterChip(
                                    selected = fps == value,
                                    onClick = { fps = value },
                                    label = { Text("${value}fps") },
                                )
                            }
                        }
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text("画质 $quality", style = MaterialTheme.typography.bodySmall)
                            Slider(
                                value = quality.toFloat(),
                                onValueChange = { quality = it.toInt().coerceIn(30, 90) },
                                valueRange = 30f..90f,
                                modifier = Modifier.weight(1f),
                            )
                        }
                    }
                }
            }

            Box(
                Modifier.fillMaxWidth().weight(1f).background(Color.Black),
                contentAlignment = Alignment.Center,
            ) {
                AndroidView(
                    factory = { context ->
                        ImageView(context).apply {
                            scaleType = ImageView.ScaleType.FIT_CENTER
                            setBackgroundColor(android.graphics.Color.BLACK)
                        }
                    },
                    update = { imageView ->
                        bitmap?.let {
                            imageView.setImageBitmap(it)
                            imageView.invalidate()
                        }
                    },
                    modifier = Modifier.fillMaxSize(),
                )
                if (!connected && camera != null) {
                    CircularProgressIndicator(color = Color.White)
                }
                Text(
                    statsText,
                    Modifier.align(Alignment.BottomStart).padding(10.dp),
                    color = Color.White,
                    style = MaterialTheme.typography.bodySmall,
                )
                TextButton(
                    onClick = { fullscreen = !fullscreen },
                    modifier = Modifier.align(Alignment.TopEnd).padding(4.dp),
                ) {
                    Text(if (fullscreen) "退出全屏" else "全屏", color = Color.White)
                }
            }

            if (!fullscreen) {
                Row(
                    Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Button(onClick = { snapshot() }, enabled = camera != null) { Text("截图保存") }
                    TextButton(onClick = { reconnect++ }) { Text("重连") }
                    TextButton(onClick = { error = null }) { Text("清除错误") }
                }
            }
        }
    }
}

/**
 * 两块可复用 Bitmap 轮换 + inBitmap，避免 10fps 解码产生大量垃圾。
 * 当前显示的永远是上一块，解码写入另一块，不会改写正在显示的像素。
 */
private class CameraFrameDecoder {
    private val buffers = arrayOfNulls<Bitmap>(2)
    private var index = 0
    private val bounds = BitmapFactory.Options()

    fun decode(bytes: ByteArray): Bitmap? {
        bounds.inJustDecodeBounds = true
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

        index = 1 - index
        val candidate = buffers[index]
        val options = BitmapFactory.Options().apply {
            inMutable = true
            inScaled = false
            if (candidate != null && !candidate.isRecycled &&
                candidate.width == bounds.outWidth && candidate.height == bounds.outHeight
            ) {
                inBitmap = candidate
            }
        }
        val bitmap = runCatching { BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options) }.getOrNull()
        buffers[index] = bitmap
        return bitmap
    }
}
