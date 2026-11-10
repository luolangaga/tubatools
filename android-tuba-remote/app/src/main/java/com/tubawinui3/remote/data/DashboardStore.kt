package com.tubawinui3.remote.data

import android.content.SharedPreferences
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID

/**
 * 副屏布局的纯逻辑存储：编解码、默认布局、新组件找空位、自动整理。
 * 不含 Android UI 依赖，可直接单元测试。
 */
object DashboardStore {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun key(pcId: String): String = "dashboard:" + pcId.ifBlank { "default" }

    fun load(prefs: SharedPreferences, pcId: String): DashboardConfig =
        decode(prefs.getString(key(pcId), null))

    fun save(prefs: SharedPreferences, pcId: String, config: DashboardConfig) {
        runCatching {
            prefs.edit().putString(key(pcId), encode(config)).apply()
        }
    }

    fun encode(config: DashboardConfig): String = json.encodeToString(config)

    fun decode(raw: String?): DashboardConfig {
        if (raw.isNullOrBlank()) return defaultConfig()
        return runCatching { json.decodeFromString<DashboardConfig>(raw) }.getOrDefault(defaultConfig())
            .let { normalize(it) }
    }

    fun defaultConfig(): DashboardConfig = DashboardConfig(
        items = listOf(
            DashboardItem(id = "seed-clock", type = "clock", x = 0, y = 0, w = 6, h = 2),
            DashboardItem(id = "seed-status", type = "status", x = 6, y = 0, w = 6, h = 2),
            DashboardItem(
                id = "seed-cpu", type = "metric", x = 0, y = 2, w = 4, h = 2,
                metric = "cpuLoad", label = "CPU 占用",
            ),
            DashboardItem(
                id = "seed-cputemp", type = "gauge", x = 4, y = 2, w = 4, h = 3,
                metric = "cpuTemp", label = "CPU 温度", gaugeMax = 110.0,
            ),
            DashboardItem(
                id = "seed-gpu", type = "metric", x = 8, y = 2, w = 4, h = 2,
                metric = "gpuTemp", label = "GPU 温度",
            ),
            DashboardItem(
                id = "seed-chart", type = "chart", x = 0, y = 5, w = 12, h = 3,
                metric = "cpuLoad", label = "CPU 占用曲线", chartPoints = 60,
            ),
        ),
    )

    fun newItem(type: String, config: DashboardConfig): DashboardItem {
        val (w, h) = when (type) {
            "clock" -> 4 to 2
            "metric" -> 3 to 2
            "chart" -> 6 to 3
            "gauge" -> 3 to 3
            "image" -> 4 to 4
            "status" -> 6 to 2
            "divider" -> 12 to 1
            else -> 4 to 1
        }
        val columns = config.columns.coerceIn(4, 24)
        val width = w.coerceAtMost(columns)
        val (x, y) = findFree(config.items, width, h, columns)
        val metric = "cpuLoad"
        return DashboardItem(
            id = UUID.randomUUID().toString().replace("-", ""),
            type = type,
            x = x,
            y = y,
            w = width,
            h = h,
            text = if (type == "text") "文字" else "",
            metric = metric,
            label = metricDef(metric)?.label ?: "",
            chartPoints = 60,
            gaugeMax = 100.0,
        )
    }

    fun autoPack(config: DashboardConfig): DashboardConfig {
        val columns = config.columns.coerceIn(4, 24)
        val placed = mutableListOf<DashboardItem>()
        config.items
            .sortedWith(compareBy({ it.y }, { it.x }))
            .forEach { item ->
                val clamped = clamp(item, columns)
                val (x, y) = findFree(placed, clamped.w, clamped.h, columns)
                placed += clamped.copy(x = x, y = y)
            }
        return config.copy(items = placed)
    }

    fun clamp(item: DashboardItem, columns: Int): DashboardItem {
        val width = item.w.coerceIn(1, columns)
        return item.copy(
            w = width,
            h = item.h.coerceIn(1, 12),
            x = item.x.coerceIn(0, columns - width),
            y = item.y.coerceAtLeast(0),
        )
    }

    private fun normalize(config: DashboardConfig): DashboardConfig {
        val columns = config.columns.coerceIn(4, 24)
        return config.copy(
            columns = columns,
            rowHeightDp = config.rowHeightDp.coerceIn(32, 120),
            items = config.items.map { clamp(it, columns) },
        )
    }

    private fun findFree(items: List<DashboardItem>, w: Int, h: Int, columns: Int): Pair<Int, Int> {
        val maxY = (items.maxOfOrNull { it.y + it.h } ?: 0).coerceAtLeast(0)
        for (y in 0..(maxY + h)) {
            for (x in 0..(columns - w)) {
                val candidate = DashboardItem(type = "probe", x = x, y = y, w = w, h = h)
                if (items.none { overlaps(candidate, it) }) return x to y
            }
        }
        return 0 to maxY
    }

    private fun overlaps(a: DashboardItem, b: DashboardItem): Boolean =
        a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y
}
