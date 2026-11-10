package com.tubawinui3.remote.ui

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import com.tubawinui3.remote.data.DownloadQueue
import com.tubawinui3.remote.data.DownloadTask
import com.tubawinui3.remote.data.TransferState

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DownloadsScreen(onBack: () -> Unit) {
    val ctx = LocalContext.current
    val tasks by DownloadQueue.tasks.collectAsState()
    val active = tasks.filter { it.state == TransferState.Queued || it.state == TransferState.Downloading }
    val history = tasks.filter { it.state != TransferState.Queued && it.state != TransferState.Downloading }

    var needStorage by remember {
        mutableStateOf(
            Build.VERSION.SDK_INT < Build.VERSION_CODES.Q &&
                ContextCompat.checkSelfPermission(ctx, Manifest.permission.WRITE_EXTERNAL_STORAGE) !=
                PackageManager.PERMISSION_GRANTED
        )
    }
    val storagePermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        needStorage = !it
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("传输队列") },
                navigationIcon = { TextButton(onClick = onBack) { Text("返回") } },
            )
        },
    ) { pad ->
        LazyColumn(
            Modifier.fillMaxSize().padding(pad),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (needStorage) {
                item {
                    Card(Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            Text("需要存储权限", style = MaterialTheme.typography.titleSmall)
                            Text(
                                "Android 9 及以下保存文件到下载目录需要存储权限。",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            TextButton(onClick = { storagePermission.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE) }) {
                                Text("授予权限")
                            }
                        }
                    }
                }
            }

            if (active.isEmpty() && history.isEmpty()) {
                item {
                    Text(
                        "暂无传输任务。电脑发来文件后，点通知里的「下载」即可；也可以在传输助手里点「保存到手机」。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            if (active.isNotEmpty()) {
                item { Text("进行中", style = MaterialTheme.typography.titleMedium) }
                items(active, key = { it.id }) { task ->
                    DownloadRow(task, active = true, context = ctx)
                }
            }
            if (history.isNotEmpty()) {
                item { Text("最近", style = MaterialTheme.typography.titleMedium) }
                items(history.take(50), key = { it.id }) { task ->
                    DownloadRow(task, active = false, context = ctx)
                }
            }
        }
    }
}

@Composable
private fun DownloadRow(task: DownloadTask, active: Boolean, context: android.content.Context) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    task.fileName,
                    style = MaterialTheme.typography.titleSmall,
                    modifier = Modifier.weight(1f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    stateText(task.state),
                    style = MaterialTheme.typography.bodySmall,
                    color = when (task.state) {
                        TransferState.Failed -> MaterialTheme.colorScheme.error
                        TransferState.Completed -> MaterialTheme.colorScheme.primary
                        else -> MaterialTheme.colorScheme.onSurfaceVariant
                    },
                )
            }

            if (active) {
                if (task.size > 0) {
                    LinearProgressIndicator(
                        progress = { (task.bytes.toFloat() / task.size).coerceIn(0f, 1f) },
                        modifier = Modifier.fillMaxWidth(),
                    )
                } else {
                    LinearProgressIndicator(Modifier.fillMaxWidth())
                }
                Text(
                    buildString {
                        append(DownloadQueue.humanSize(task.bytes))
                        if (task.size > 0) append(" / ").append(DownloadQueue.humanSize(task.size))
                        if (task.speed > 0) append(" · ").append(DownloadQueue.speedText(task.speed))
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                Text(
                    buildString {
                        if (task.size > 0) append(DownloadQueue.humanSize(task.size))
                        task.localUri?.let { if (isNotEmpty()) append(" · "); append("已保存") }
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            task.error?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }

            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                if (active) {
                    TextButton(onClick = { DownloadQueue.cancel(task.id) }) { Text("取消") }
                } else {
                    if (task.state == TransferState.Failed || task.state == TransferState.Cancelled) {
                        TextButton(onClick = { DownloadQueue.retry(task.id) }) { Text("重试") }
                    }
                    if (task.state == TransferState.Completed) {
                        TextButton(onClick = { DownloadQueue.open(context, task) }) { Text("打开") }
                    }
                }
            }
        }
    }
}

private fun stateText(state: TransferState): String = when (state) {
    TransferState.Queued -> "等待中"
    TransferState.Downloading -> "下载中"
    TransferState.Completed -> "已完成"
    TransferState.Failed -> "失败"
    TransferState.Cancelled -> "已取消"
}
