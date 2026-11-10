package com.tubawinui3.remote.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.MetricDef
import com.tubawinui3.remote.data.MetricDefs
import com.tubawinui3.remote.data.MonitorData
import com.tubawinui3.remote.data.MonitorHistorySize
import kotlinx.coroutines.delay
import java.util.Locale

private val FpsKeys = setOf("fps", "fpsLow1", "frameTime")

@OptIn(ExperimentalMaterial3Api::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
fun MonitorScreen(client: ApiClient, onBack: () -> Unit) {
    var selectedRaw by rememberSaveable { mutableStateOf("cpuLoad,cpuTemp,gpuLoad,memLoad") }
    val selected = selectedRaw.split(',').filter { it.isNotEmpty() }.toSet()
    var charts by rememberSaveable { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    val history = remember { mutableStateListOf<MonitorData>() }
    val needFps = selected.any { it in FpsKeys }

    LaunchedEffect(needFps) {
        while (true) {
            try {
                history.add(client.monitor(needFps))
                if (history.size > MonitorHistorySize) history.removeAt(0)
                error = null
            } catch (e: ApiException) {
                error = e.message
            }
            delay(1000)
        }
    }

    Scaffold(topBar = {
        TopAppBar(title = { Text("实时硬件监控") }, navigationIcon = { TextButton(onClick = onBack) { Text("返回") } })
    }) { pad ->
        LazyColumn(
            Modifier.fillMaxSize().padding(pad),
            contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("选择要展示的监控项", style = MaterialTheme.typography.titleSmall)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        MetricDefs.forEach { m ->
                            FilterChip(
                                selected = m.key in selected,
                                onClick = {
                                    val next = if (m.key in selected) selected - m.key else selected + m.key
                                    selectedRaw = MetricDefs.map { it.key }.filter { it in next }.joinToString(",")
                                },
                                label = { Text(m.label) },
                            )
                        }
                    }
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Switch(checked = charts, onCheckedChange = { charts = it })
                        Text("绘制曲线图")
                    }
                    if (needFps) Text("FPS 为电脑前台游戏/程序的帧率，无画面输出时显示 —", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                }
            }
            items(MetricDefs.filter { it.key in selected }, key = { it.key }) { m -> MetricCard(m, history, charts) }
        }
    }
}

@Composable
private fun MetricCard(m: MetricDef, history: List<MonitorData>, chart: Boolean) {
    val values = history.map { m.get(it) }
    val last = values.lastOrNull() ?: -1.0
    val color = MaterialTheme.colorScheme.primary
    val grid = MaterialTheme.colorScheme.outlineVariant
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                Text(m.label, style = MaterialTheme.typography.titleSmall)
                Text(
                    if (last < 0) "—" else formatValue(last) + (if (m.unit.isEmpty()) "" else " ${m.unit}"),
                    style = MaterialTheme.typography.titleMedium, color = color,
                )
            }
            if (chart) {
                val top = m.max ?: maxOf(values.maxOrNull() ?: 1.0, 1.0) * 1.15
                Canvas(Modifier.fillMaxWidth().height(90.dp)) {
                    for (i in 0..2) {
                        val y = size.height * i / 2f
                        drawLine(grid, Offset(0f, y), Offset(size.width, y), strokeWidth = 1f)
                    }
                    val path = Path()
                    var started = false
                    values.forEachIndexed { i, v ->
                        if (v < 0) { started = false; return@forEachIndexed }
                        val x = size.width * (MonitorHistorySize - values.size + i) / (MonitorHistorySize - 1f)
                        val y = size.height * (1f - (v / top).toFloat().coerceIn(0f, 1f))
                        if (!started) { path.moveTo(x, y); started = true } else path.lineTo(x, y)
                    }
                    drawPath(path, color, style = Stroke(width = 3f))
                }
            }
        }
    }
}

private fun formatValue(v: Double) = if (v >= 100) String.format(Locale.US, "%.0f", v) else String.format(Locale.US, "%.1f", v)
