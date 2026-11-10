package com.tubawinui3.remote.data

/** 可选监控项：[max] 为 null 时纵轴按历史最大值自适应。 */
class MetricDef(
    val key: String,
    val label: String,
    val unit: String,
    val max: Double?,
    val get: (MonitorData) -> Double,
)

/** 监控折线保留的历史点数（约 2 分钟）。 */
const val MonitorHistorySize = 120

val MetricDefs = listOf(
    MetricDef("cpuLoad", "CPU 占用", "%", 100.0) { it.cpuLoad },
    MetricDef("cpuTemp", "CPU 温度", "℃", 110.0) { it.cpuTemp },
    MetricDef("cpuClock", "CPU 频率", "MHz", null) { it.cpuClock },
    MetricDef("cpuPower", "CPU 功耗", "W", null) { it.cpuPower },
    MetricDef("gpuLoad", "GPU 占用", "%", 100.0) { it.gpuLoad },
    MetricDef("gpuTemp", "GPU 温度", "℃", 110.0) { it.gpuTemp },
    MetricDef("gpuClock", "GPU 频率", "MHz", null) { it.gpuClock },
    MetricDef("gpuPower", "GPU 功耗", "W", null) { it.gpuPower },
    MetricDef("gpuVram", "显存占用", "GB", null) { it.gpuVramUsedGB },
    MetricDef("gpuVramLoad", "显存占用率", "%", 100.0) { it.gpuVramLoad },
    MetricDef("memLoad", "内存占用", "%", 100.0) { it.memLoad },
    MetricDef("memUsed", "内存已用", "GB", null) { it.memUsedGB },
    MetricDef("diskRead", "磁盘读", "MB/s", null) { it.diskReadMBs },
    MetricDef("diskWrite", "磁盘写", "MB/s", null) { it.diskWriteMBs },
    MetricDef("diskTemp", "磁盘温度", "℃", 110.0) { it.diskTemp },
    MetricDef("netDown", "网络下载", "MB/s", null) { it.netDownMBs },
    MetricDef("netUp", "网络上传", "MB/s", null) { it.netUpMBs },
    MetricDef("batPercent", "电池电量", "%", 100.0) { it.batPercent },
    MetricDef("fps", "FPS", "", null) { it.fps },
    MetricDef("fpsLow1", "FPS 1% Low", "", null) { it.fpsLow1 },
    MetricDef("frameTime", "帧时间", "ms", null) { it.frameTimeMs },
)

fun metricDef(key: String): MetricDef? = MetricDefs.firstOrNull { it.key == key }

fun formatMetricValue(value: Double, decimals: Int): String {
    if (value < 0) return "—"
    if (decimals <= 0) return "%.0f".format(value)
    return "%.${decimals.coerceIn(1, 2)}f".format(value)
}
