package com.tubawinui3.remote.ui

import android.app.Activity
import android.content.pm.ActivityInfo
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.DashboardConfig
import com.tubawinui3.remote.data.DashboardImages
import com.tubawinui3.remote.data.DashboardItem
import com.tubawinui3.remote.data.DashboardStore
import com.tubawinui3.remote.data.MetricDefs
import com.tubawinui3.remote.data.MonitorData
import com.tubawinui3.remote.data.MonitorHistorySize
import com.tubawinui3.remote.data.formatMetricValue
import com.tubawinui3.remote.data.metricDef
import kotlinx.coroutines.delay
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID
import kotlin.math.roundToInt

private val DashboardGap = 8.dp
private val PresetColors = listOf(
    "" to "默认",
    "#FFFFFF" to "白",
    "#B0BEC5" to "灰",
    "#4FC3F7" to "蓝",
    "#66BB6A" to "绿",
    "#FFD54F" to "黄",
    "#EF5350" to "红",
    "#4DD0E1" to "青",
)

private fun newId(): String = UUID.randomUUID().toString().replace("-", "")

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DashboardScreen(client: ApiClient, pcId: String, pcName: String, onBack: () -> Unit) {
    val ctx = LocalContext.current
    val prefs = remember { ctx.getSharedPreferences("remote", 0) }
    var config by remember(pcId) { mutableStateOf(DashboardStore.load(prefs, pcId)) }
    var selectedId by remember { mutableStateOf<String?>(null) }
    var editingId by remember { mutableStateOf<String?>(null) }
    var showPicker by remember { mutableStateOf(false) }
    var display by rememberSaveable { mutableStateOf(false) }

    val selected = config.items.firstOrNull { it.id == selectedId }
    val editing = config.items.firstOrNull { it.id == editingId }

    fun save(next: DashboardConfig) {
        config = next
        DashboardStore.save(prefs, pcId, next)
        DashboardImages.gc(ctx, pcId, next)
    }

    fun updateItem(item: DashboardItem) {
        save(config.copy(items = config.items.map { if (it.id == item.id) item else it }))
    }

    fun addItem(type: String) {
        val item = DashboardStore.newItem(type, config)
        save(config.copy(items = config.items + item))
        selectedId = item.id
        editingId = item.id
    }

    fun duplicate(item: DashboardItem) {
        val copy = item.copy(
            id = newId(),
            x = (item.x + 1).coerceAtMost((config.columns - item.w).coerceAtLeast(0)),
            y = item.y + item.h,
        )
        save(config.copy(items = config.items + copy))
        selectedId = copy.id
        editingId = copy.id
    }

    fun moveZ(item: DashboardItem, delta: Int) {
        val list = config.items.toMutableList()
        val index = list.indexOfFirst { it.id == item.id }
        if (index < 0) return
        val target = (index + delta).coerceIn(0, list.size - 1)
        if (target == index) return
        val moved = list.removeAt(index)
        list.add(target, moved)
        save(config.copy(items = list))
    }

    if (display) {
        DashboardDisplay(client, pcId, pcName, config, onExit = { display = false })
        return
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (pcName.isBlank()) "副屏模式" else "副屏模式 · $pcName") },
                navigationIcon = { TextButton(onClick = onBack) { Text("返回") } },
                actions = {
                    TextButton(onClick = { showPicker = true }) { Text("添加") }
                    TextButton(onClick = { save(DashboardStore.autoPack(config)) }) { Text("整理") }
                    TextButton(enabled = selected != null, onClick = { editingId = selectedId }) { Text("编辑") }
                    TextButton(onClick = { display = true }) { Text("全屏") }
                },
            )
        },
    ) { pad ->
        Column(Modifier.fillMaxSize().padding(pad)) {
            Text(
                "长按组件拖动，拖动右下角缩放；点选后按「编辑」改属性。允许重叠，图片可以放大后置于底层当背景。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
            )
            DashboardGrid(
                config = config,
                pcId = pcId,
                selectedId = selectedId,
                onSelect = { selectedId = it },
                onMove = { id, x, y ->
                    config.items.firstOrNull { it.id == id }?.let { item ->
                        if (item.x != x || item.y != y) updateItem(item.copy(x = x, y = y))
                    }
                },
                onResize = { id, w, h ->
                    config.items.firstOrNull { it.id == id }?.let { item ->
                        if (item.w != w || item.h != h) updateItem(item.copy(w = w, h = h))
                    }
                },
                modifier = Modifier.weight(1f),
            )
        }
    }

    if (showPicker) {
        ComponentPicker(
            onPick = { type ->
                showPicker = false
                addItem(type)
            },
            onDismiss = { showPicker = false },
        )
    }

    if (editing != null) {
        PropertySheet(
            item = editing,
            columns = config.columns,
            pcId = pcId,
            onChange = { updateItem(it) },
            onDelete = { item ->
                save(config.copy(items = config.items.filter { it.id != item.id }))
                editingId = null
                selectedId = null
            },
            onDuplicate = { duplicate(it) },
            onMoveZ = { item, delta -> moveZ(item, delta) },
            onDismiss = { editingId = null },
        )
    }
}

// ───────────────────────── 编辑网格 ─────────────────────────

@Composable
private fun DashboardGrid(
    config: DashboardConfig,
    pcId: String,
    selectedId: String?,
    onSelect: (String) -> Unit,
    onMove: (String, Int, Int) -> Unit,
    onResize: (String, Int, Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val scroll = rememberScrollState()
    val columns = config.columns.coerceIn(4, 24)
    val rowHeight = config.rowHeightDp.dp

    BoxWithConstraints(modifier.fillMaxWidth()) {
        val inner = maxWidth - DashboardGap * 2
        val cellWidth = if (inner > DashboardGap * (columns - 1)) {
            (inner - DashboardGap * (columns - 1)) / columns
        } else {
            0.dp
        }
        val rows = maxOf(config.items.maxOfOrNull { it.y + it.h } ?: 1, 8)
        val height = rowHeight * rows + DashboardGap * (rows + 1)

        Box(
            Modifier
                .fillMaxWidth()
                .height(height)
                .verticalScroll(scroll)
                .padding(DashboardGap),
        ) {
            config.items.forEach { item ->
                DashboardEditorItem(
                    item = item,
                    pcId = pcId,
                    cellWidth = cellWidth,
                    rowHeight = rowHeight,
                    columns = columns,
                    selected = item.id == selectedId,
                    onSelect = { onSelect(item.id) },
                    onMove = { x, y -> onMove(item.id, x, y) },
                    onResize = { w, h -> onResize(item.id, w, h) },
                )
            }
        }
    }
}

@Composable
private fun DashboardEditorItem(
    item: DashboardItem,
    pcId: String,
    cellWidth: Dp,
    rowHeight: Dp,
    columns: Int,
    selected: Boolean,
    onSelect: () -> Unit,
    onMove: (Int, Int) -> Unit,
    onResize: (Int, Int) -> Unit,
) {
    val density = LocalDensity.current
    val stepX = with(density) { (cellWidth + DashboardGap).toPx() }
    val stepY = with(density) { (rowHeight + DashboardGap).toPx() }

    var dragX by remember(item.id) { mutableStateOf(0f) }
    var dragY by remember(item.id) { mutableStateOf(0f) }
    var resizeW by remember(item.id) { mutableStateOf(0f) }
    var resizeH by remember(item.id) { mutableStateOf(0f) }

    val baseW = cellWidth * item.w + DashboardGap * (item.w - 1)
    val baseH = rowHeight * item.h + DashboardGap * (item.h - 1)
    val minResizeW = -(item.w - 1) * stepX
    val minResizeH = -(item.h - 1) * stepY

    val x = cellWidth * item.x + DashboardGap * item.x
    val y = rowHeight * item.y + DashboardGap * item.y

    Box(
        Modifier
            .offset(x = x + with(density) { dragX.toDp() }, y = y + with(density) { dragY.toDp() })
            .size(width = baseW + with(density) { resizeW.toDp() }, height = baseH + with(density) { resizeH.toDp() })
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.35f))
            .border(
                width = if (selected) 2.dp else 1.dp,
                color = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant,
                shape = RoundedCornerShape(12.dp),
            )
            .clickable { onSelect() }
            .pointerInput(item.id) {
                detectDragGesturesAfterLongPress(
                    onDragStart = { onSelect() },
                    onDragEnd = {
                        val dx = (dragX / stepX).roundToInt()
                        val dy = (dragY / stepY).roundToInt()
                        val nx = (item.x + dx).coerceIn(0, (columns - item.w).coerceAtLeast(0))
                        val ny = (item.y + dy).coerceAtLeast(0)
                        dragX = 0f
                        dragY = 0f
                        if (nx != item.x || ny != item.y) onMove(nx, ny)
                    },
                    onDragCancel = {
                        dragX = 0f
                        dragY = 0f
                    },
                    onDrag = { change, amount ->
                        change.consume()
                        dragX += amount.x
                        dragY += amount.y
                    },
                )
            },
    ) {
        DashboardItemContent(
            item = item,
            data = null,
            history = emptyList(),
            pcName = "",
            connected = true,
            editing = true,
            pcId = pcId,
            modifier = Modifier.fillMaxSize().padding(6.dp),
        )

        if (selected) {
            Box(
                Modifier
                    .align(Alignment.BottomEnd)
                    .padding(4.dp)
                    .size(28.dp)
                    .background(MaterialTheme.colorScheme.primary, CircleShape)
                    .pointerInput(item.id) {
                        detectDragGestures(
                            onDragEnd = {
                                val dw = (resizeW / stepX).roundToInt()
                                val dh = (resizeH / stepY).roundToInt()
                                val nw = (item.w + dw).coerceIn(1, (columns - item.x).coerceAtLeast(1))
                                val nh = (item.h + dh).coerceIn(1, 12)
                                resizeW = 0f
                                resizeH = 0f
                                if (nw != item.w || nh != item.h) onResize(nw, nh)
                            },
                            onDragCancel = {
                                resizeW = 0f
                                resizeH = 0f
                            },
                            onDrag = { change, amount ->
                                change.consume()
                                resizeW = (resizeW + amount.x).coerceAtLeast(minResizeW)
                                resizeH = (resizeH + amount.y).coerceAtLeast(minResizeH)
                            },
                        )
                    },
                contentAlignment = Alignment.Center,
            ) {
                Text("◢", color = Color.White, fontSize = 12.sp)
            }
        }
    }
}

// ───────────────────────── 组件选择 / 属性面板 ─────────────────────────

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
private fun ComponentPicker(onPick: (String) -> Unit, onDismiss: () -> Unit) {
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(
            Modifier.fillMaxWidth().padding(16.dp).navigationBarsPadding(),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text("添加组件", style = MaterialTheme.typography.titleMedium)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(
                    "text" to "文字",
                    "clock" to "时钟",
                    "metric" to "数值",
                    "chart" to "曲线",
                    "gauge" to "环形表",
                    "status" to "状态",
                    "image" to "图片",
                    "divider" to "分隔线",
                ).forEach { (type, label) ->
                    FilterChip(selected = false, onClick = { onPick(type) }, label = { Text(label) })
                }
            }
            Text(
                "图片可以调大后「置底」当背景；数值/曲线/环形表可绑定任意监控项。",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
private fun PropertySheet(
    item: DashboardItem,
    columns: Int,
    pcId: String,
    onChange: (DashboardItem) -> Unit,
    onDelete: (DashboardItem) -> Unit,
    onDuplicate: (DashboardItem) -> Unit,
    onMoveZ: (DashboardItem, Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val ctx = LocalContext.current
    val sheet = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val imagePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        if (uri != null) {
            DashboardImages.import(ctx, pcId, uri)?.let { name -> onChange(item.copy(imageName = name)) }
        }
    }
    val def = metricDef(item.metric)

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheet) {
        Column(
            Modifier
                .fillMaxWidth()
                .padding(16.dp)
                .navigationBarsPadding()
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(typeLabel(item.type), style = MaterialTheme.typography.titleMedium)

            when (item.type) {
                "text" -> {
                    OutlinedTextField(
                        value = item.text,
                        onValueChange = { onChange(item.copy(text = it)) },
                        label = { Text("文字") },
                        modifier = Modifier.fillMaxWidth(),
                        maxLines = 4,
                    )
                    Text("字号 ${item.textSize.toInt()}", style = MaterialTheme.typography.bodySmall)
                    Slider(
                        value = item.textSize,
                        onValueChange = { onChange(item.copy(textSize = it)) },
                        valueRange = 10f..48f,
                    )
                    ChipRow(
                        title = "对齐",
                        options = listOf("left" to "左", "center" to "中", "right" to "右"),
                        selected = item.align,
                        onSelect = { onChange(item.copy(align = it)) },
                    )
                }
                "metric", "chart", "gauge" -> {
                    OutlinedTextField(
                        value = item.label,
                        onValueChange = { onChange(item.copy(label = it)) },
                        label = { Text("标签（留空用监控项名称）") },
                        modifier = Modifier.fillMaxWidth(),
                        singleLine = true,
                    )
                    Text("监控项", style = MaterialTheme.typography.bodySmall)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        MetricDefs.forEach { metric ->
                            FilterChip(
                                selected = item.metric == metric.key,
                                onClick = { onChange(item.copy(metric = metric.key)) },
                                label = { Text(metric.label) },
                            )
                        }
                    }
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("显示标签", style = MaterialTheme.typography.bodySmall)
                        Switch(checked = item.showLabel, onCheckedChange = { onChange(item.copy(showLabel = it)) })
                        Text("显示单位", style = MaterialTheme.typography.bodySmall)
                        Switch(checked = item.showUnit, onCheckedChange = { onChange(item.copy(showUnit = it)) })
                    }
                    ChipRow(
                        title = "小数位",
                        options = listOf("0" to "0", "1" to "1", "2" to "2"),
                        selected = item.decimals.toString(),
                        onSelect = { onChange(item.copy(decimals = it.toIntOrNull() ?: 1)) },
                    )
                    if (item.type == "chart") {
                        ChipRow(
                            title = "曲线点数",
                            options = listOf("30" to "30", "60" to "60", "120" to "120"),
                            selected = item.chartPoints.toString(),
                            onSelect = { onChange(item.copy(chartPoints = it.toIntOrNull() ?: 60)) },
                        )
                    }
                    if (item.type == "gauge") {
                        ChipRow(
                            title = "表盘上限",
                            options = listOf("80" to "80", "100" to "100", "110" to "110", "120" to "120"),
                            selected = item.gaugeMax.toInt().toString(),
                            onSelect = { onChange(item.copy(gaugeMax = it.toDoubleOrNull() ?: 100.0)) },
                        )
                    }
                    def?.let {
                        Text(
                            "当前监控项：${it.label}" + if (it.unit.isBlank()) "" else "（${it.unit}）",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                "image" -> {
                    Button(onClick = { imagePicker.launch("image/*") }) { Text("选择图片") }
                    Text(
                        if (item.imageName.isBlank()) "未选择图片" else "已选择：${item.imageName}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    ChipRow(
                        title = "填充方式",
                        options = listOf("fit" to "完整显示", "crop" to "裁剪填满"),
                        selected = item.imageFit,
                        onSelect = { onChange(item.copy(imageFit = it)) },
                    )
                }
                "clock" -> {
                    Text("显示时间与日期；日期可以在「显示标签」里关闭。", style = MaterialTheme.typography.bodySmall)
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("显示日期", style = MaterialTheme.typography.bodySmall)
                        Switch(checked = item.showLabel, onCheckedChange = { onChange(item.copy(showLabel = it)) })
                    }
                }
            }

            if (item.type != "image" && item.type != "divider") {
                Text("颜色", style = MaterialTheme.typography.bodySmall)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    PresetColors.forEach { (hex, label) ->
                        FilterChip(
                            selected = item.color == hex,
                            onClick = { onChange(item.copy(color = hex)) },
                            label = { Text(label) },
                        )
                    }
                }
            }

            Text("位置与尺寸", style = MaterialTheme.typography.bodySmall)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                NumberStepper("X", item.x, 0..(columns - item.w).coerceAtLeast(0)) { onChange(item.copy(x = it)) }
                NumberStepper("Y", item.y, 0..200) { onChange(item.copy(y = it)) }
                NumberStepper("宽", item.w, 1..(columns - item.x).coerceAtLeast(1)) { onChange(item.copy(w = it)) }
                NumberStepper("高", item.h, 1..12) { onChange(item.copy(h = it)) }
            }

            Text("层级", style = MaterialTheme.typography.bodySmall)
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                TextButton(onClick = { onMoveZ(item, 1000) }) { Text("置顶") }
                TextButton(onClick = { onMoveZ(item, 1) }) { Text("上移") }
                TextButton(onClick = { onMoveZ(item, -1) }) { Text("下移") }
                TextButton(onClick = { onMoveZ(item, -1000) }) { Text("置底") }
            }

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { onDuplicate(item) }) { Text("复制") }
                TextButton(onClick = { onDelete(item) }) {
                    Text("删除", color = MaterialTheme.colorScheme.error)
                }
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ChipRow(
    title: String,
    options: List<Pair<String, String>>,
    selected: String,
    onSelect: (String) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(title, style = MaterialTheme.typography.bodySmall)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            options.forEach { (value, label) ->
                FilterChip(
                    selected = selected == value,
                    onClick = { onSelect(value) },
                    label = { Text(label) },
                )
            }
        }
    }
}

@Composable
private fun NumberStepper(label: String, value: Int, range: IntRange, onChange: (Int) -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(label, style = MaterialTheme.typography.bodySmall)
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = { onChange((value - 1).coerceIn(range)) }) { Text("−") }
            Text("$value")
            TextButton(onClick = { onChange((value + 1).coerceIn(range)) }) { Text("+") }
        }
    }
}

// ───────────────────────── 全屏展示 ─────────────────────────

@Composable
private fun DashboardDisplay(
    client: ApiClient,
    pcId: String,
    pcName: String,
    config: DashboardConfig,
    onExit: () -> Unit,
) {
    val ctx = LocalContext.current
    val view = LocalView.current
    val activity = ctx as? Activity
    var history by remember { mutableStateOf<List<MonitorData>>(emptyList()) }
    var connected by remember { mutableStateOf(false) }
    var landscape by remember { mutableStateOf(false) }
    val needFps = config.items.any {
        (it.type == "metric" || it.type == "chart" || it.type == "gauge") &&
            it.metric in setOf("fps", "fpsLow1", "frameTime")
    }

    LaunchedEffect(needFps) {
        while (true) {
            try {
                val data = client.monitor(needFps)
                history = (history + data).takeLast(MonitorHistorySize)
                connected = true
            } catch (_: ApiException) {
                connected = false
            }
            delay(1000)
        }
    }

    DisposableEffect(Unit) {
        val window = activity?.window
        val controller = window?.let { WindowCompat.getInsetsController(it, view) }
        if (window != null) {
            WindowCompat.setDecorFitsSystemWindows(window, false)
            controller?.hide(WindowInsetsCompat.Type.systemBars())
            controller?.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
        onDispose {
            if (window != null) {
                controller?.show(WindowInsetsCompat.Type.systemBars())
                WindowCompat.setDecorFitsSystemWindows(window, true)
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
            activity?.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        }
    }

    LaunchedEffect(landscape) {
        activity?.requestedOrientation = if (landscape) {
            ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        } else {
            ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        }
    }

    BackHandler { onExit() }

    Box(Modifier.fillMaxSize().background(dashboardBrush(config.background))) {
        DashboardCanvas(
            config = config,
            data = history.lastOrNull(),
            history = history,
            pcName = pcName,
            connected = connected,
            pcId = pcId,
            modifier = Modifier.fillMaxSize(),
        )

        if (!connected) {
            Text(
                "重连中…",
                Modifier.align(Alignment.TopStart).padding(12.dp),
                color = Color(0xFFFFB4AB),
                style = MaterialTheme.typography.bodySmall,
            )
        }

        Row(
            Modifier
                .align(Alignment.BottomEnd)
                .padding(10.dp)
                .clip(RoundedCornerShape(20.dp))
                .background(Color(0x66000000))
                .padding(horizontal = 6.dp),
        ) {
            TextButton(onClick = { landscape = !landscape }) {
                Text(if (landscape) "跟随系统" else "锁定横屏", color = Color.White)
            }
            TextButton(onClick = onExit) { Text("退出", color = Color.White) }
        }
    }
}

@Composable
private fun DashboardCanvas(
    config: DashboardConfig,
    data: MonitorData?,
    history: List<MonitorData>,
    pcName: String,
    connected: Boolean,
    pcId: String,
    modifier: Modifier = Modifier,
) {
    val columns = config.columns.coerceIn(4, 24)
    BoxWithConstraints(modifier) {
        val rows = maxOf(config.items.maxOfOrNull { it.y + it.h } ?: 1, 1)
        val innerWidth = maxWidth - DashboardGap * 2
        val innerHeight = maxHeight - DashboardGap * 2
        val cellWidth = if (innerWidth > DashboardGap * (columns - 1)) {
            (innerWidth - DashboardGap * (columns - 1)) / columns
        } else {
            0.dp
        }
        val cellHeight = if (innerHeight > DashboardGap * (rows - 1)) {
            minOf(config.rowHeightDp.dp, (innerHeight - DashboardGap * (rows - 1)) / rows)
                .coerceAtLeast(24.dp)
        } else {
            24.dp
        }

        Box(Modifier.fillMaxSize().padding(DashboardGap)) {
            config.items.forEach { item ->
                val x = cellWidth * item.x + DashboardGap * item.x
                val y = cellHeight * item.y + DashboardGap * item.y
                val w = cellWidth * item.w + DashboardGap * (item.w - 1)
                val h = cellHeight * item.h + DashboardGap * (item.h - 1)
                val card = item.type != "image" && item.type != "divider"
                Box(
                    Modifier
                        .offset(x, y)
                        .size(w, h)
                        .then(
                            if (card) {
                                Modifier
                                    .clip(RoundedCornerShape(12.dp))
                                    .background(Color(0x2EFFFFFF))
                            } else {
                                Modifier
                            }
                        ),
                ) {
                    DashboardItemContent(
                        item = item,
                        data = data,
                        history = history,
                        pcName = pcName,
                        connected = connected,
                        editing = false,
                        pcId = pcId,
                        modifier = Modifier.fillMaxSize().padding(if (card) 8.dp else 0.dp),
                    )
                }
            }
        }
    }
}

@Composable
private fun DashboardItemContent(
    item: DashboardItem,
    data: MonitorData?,
    history: List<MonitorData>,
    pcName: String,
    connected: Boolean,
    editing: Boolean,
    pcId: String,
    modifier: Modifier = Modifier,
) {
    val color = parseColor(item.color) ?: MaterialTheme.colorScheme.onSurface
    val def = metricDef(item.metric)
    val value = if (data != null && def != null) def.get(data) else -1.0
    val label = item.label.ifBlank { def?.label ?: item.metric }

    Box(modifier, contentAlignment = Alignment.Center) {
        when (item.type) {
            "clock" -> ClockContent(item, color)
            "text" -> Text(
                item.text.ifBlank { if (editing) "文字" else "" },
                fontSize = item.textSize.sp,
                color = color,
                textAlign = when (item.align) {
                    "center" -> TextAlign.Center
                    "right" -> TextAlign.Right
                    else -> TextAlign.Left
                },
                modifier = Modifier.fillMaxWidth(),
            )
            "metric" -> Column(horizontalAlignment = Alignment.CenterHorizontally) {
                if (item.showLabel) {
                    Text(label, style = MaterialTheme.typography.bodySmall, color = color.copy(alpha = 0.7f))
                }
                Row(verticalAlignment = Alignment.Bottom) {
                    Text(
                        formatMetricValue(value, item.decimals),
                        fontSize = 28.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = color,
                    )
                    if (item.showUnit && !def?.unit.isNullOrBlank()) {
                        Text(
                            " ${def.unit}",
                            style = MaterialTheme.typography.bodySmall,
                            color = color.copy(alpha = 0.7f),
                            modifier = Modifier.padding(bottom = 4.dp),
                        )
                    }
                }
            }
            "chart" -> Column(Modifier.fillMaxSize()) {
                if (item.showLabel) {
                    Text(label, style = MaterialTheme.typography.bodySmall, color = color.copy(alpha = 0.7f))
                }
                val values = history.takeLast(item.chartPoints.coerceIn(2, MonitorHistorySize))
                    .map { def?.get(it) ?: -1.0 }
                MetricChart(values, def?.max, color, Modifier.fillMaxWidth().weight(1f))
            }
            "gauge" -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Gauge(value, item.gaugeMax, color, Modifier.fillMaxSize())
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        formatMetricValue(value, item.decimals),
                        fontSize = 24.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = color,
                    )
                    if (item.showLabel) {
                        Text(label, style = MaterialTheme.typography.bodySmall, color = color.copy(alpha = 0.7f))
                    }
                }
            }
            "status" -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Box(
                    Modifier.size(10.dp).background(
                        if (connected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error,
                        CircleShape,
                    )
                )
                Column {
                    Text(pcName.ifBlank { "电脑" }, style = MaterialTheme.typography.bodyMedium, color = color)
                    Text(
                        if (connected) "已连接" else "重连中",
                        style = MaterialTheme.typography.bodySmall,
                        color = color.copy(alpha = 0.7f),
                    )
                }
            }
            "image" -> ImageContent(item, pcId, editing)
            "divider" -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Box(Modifier.fillMaxWidth().height(2.dp).background(color.copy(alpha = 0.6f)))
            }
            else -> Text("未知组件", style = MaterialTheme.typography.bodySmall, color = color)
        }
    }
}

@Composable
private fun ClockContent(item: DashboardItem, color: Color) {
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = LocalDateTime.now()
            delay(1000)
        }
    }
    val time = remember(now) { now.format(DateTimeFormatter.ofPattern("HH:mm:ss")) }
    val date = remember(now) { now.format(DateTimeFormatter.ofPattern("M月d日 EEEE", Locale.CHINA)) }
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Text(time, fontSize = 26.sp, fontWeight = FontWeight.SemiBold, color = color)
        if (item.showLabel) {
            Text(date, style = MaterialTheme.typography.bodySmall, color = color.copy(alpha = 0.7f))
        }
    }
}

@Composable
private fun ImageContent(item: DashboardItem, pcId: String, editing: Boolean) {
    val ctx = LocalContext.current
    val bitmap = remember(item.imageName, pcId) { DashboardImages.load(ctx, pcId, item.imageName) }
    if (bitmap != null) {
        Image(
            bitmap.asImageBitmap(),
            contentDescription = null,
            contentScale = if (item.imageFit == "crop") ContentScale.Crop else ContentScale.Fit,
            modifier = Modifier.fillMaxSize().clip(RoundedCornerShape(12.dp)),
        )
    } else if (editing) {
        Text("选择图片", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun MetricChart(values: List<Double>, max: Double?, color: Color, modifier: Modifier) {
    val grid = MaterialTheme.colorScheme.outlineVariant
    Canvas(modifier) {
        for (i in 0..2) {
            val y = size.height * i / 2f
            drawLine(grid, Offset(0f, y), Offset(size.width, y), strokeWidth = 1f)
        }
        if (values.size < 2) return@Canvas
        val valid = values.filter { it >= 0 }
        val top = max ?: maxOf(valid.maxOrNull() ?: 1.0, 1.0) * 1.15
        val path = Path()
        var started = false
        values.forEachIndexed { index, value ->
            if (value < 0) {
                started = false
                return@forEachIndexed
            }
            val x = size.width * (MonitorHistorySize - values.size + index) / (MonitorHistorySize - 1f)
            val y = size.height * (1f - (value / top).toFloat().coerceIn(0f, 1f))
            if (!started) {
                path.moveTo(x, y)
                started = true
            } else {
                path.lineTo(x, y)
            }
        }
        drawPath(path, color, style = Stroke(width = 2.5f))
    }
}

@Composable
private fun Gauge(value: Double, max: Double, color: Color, modifier: Modifier) {
    val background = MaterialTheme.colorScheme.outlineVariant
    Canvas(modifier) {
        val stroke = 10.dp.toPx()
        val d = minOf(size.width, size.height)
        if (d <= stroke * 2) return@Canvas
        val topLeft = Offset((size.width - d) / 2 + stroke / 2, (size.height - d) / 2 + stroke / 2)
        val arcSize = Size(d - stroke, d - stroke)
        drawArc(
            background,
            135f,
            270f,
            false,
            topLeft,
            arcSize,
            style = Stroke(stroke, cap = StrokeCap.Round),
        )
        val fraction = if (value < 0 || max <= 0) 0f else (value / max).toFloat().coerceIn(0f, 1f)
        if (fraction > 0f) {
            drawArc(
                color,
                135f,
                270f * fraction,
                false,
                topLeft,
                arcSize,
                style = Stroke(stroke, cap = StrokeCap.Round),
            )
        }
    }
}

// ───────────────────────── 工具 ─────────────────────────

private fun typeLabel(type: String): String = when (type) {
    "text" -> "文字"
    "clock" -> "时钟"
    "metric" -> "数值"
    "chart" -> "曲线"
    "gauge" -> "环形表"
    "status" -> "状态"
    "image" -> "图片"
    "divider" -> "分隔线"
    else -> "组件"
}

private fun parseColor(value: String): Color? {
    if (value.isBlank()) return null
    return runCatching {
        val hex = value.removePrefix("#")
        when (hex.length) {
            6 -> Color(0xFF000000L or hex.toLong(16))
            8 -> Color(hex.toLong(16))
            else -> null
        }
    }.getOrNull()
}

private fun dashboardBrush(background: String): Brush = when (background) {
    "black" -> SolidColor(Color.Black)
    "gradient" -> Brush.verticalGradient(
        listOf(Color(0xFF071A2B), Color(0xFF0F3B5C), Color(0xFF071018))
    )
    else -> Brush.verticalGradient(
        listOf(Color(0xFF0B0F14), Color(0xFF121A22))
    )
}
