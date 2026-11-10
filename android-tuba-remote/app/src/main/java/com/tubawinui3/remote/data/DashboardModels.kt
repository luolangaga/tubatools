package com.tubawinui3.remote.data

import kotlinx.serialization.Serializable

/** 副屏布局：12 列网格 + 以网格为单位的绝对定位组件。 */
@Serializable
data class DashboardConfig(
    val version: Int = 1,
    val columns: Int = 12,
    val rowHeightDp: Int = 56,
    val background: String = "dark",
    val items: List<DashboardItem> = emptyList(),
)

@Serializable
data class DashboardItem(
    val id: String = "",
    /** text / clock / metric / chart / gauge / status / image / divider */
    val type: String = "text",
    val x: Int = 0,
    val y: Int = 0,
    val w: Int = 4,
    val h: Int = 2,
    val text: String = "",
    val textSize: Float = 16f,
    val color: String = "",
    val align: String = "left",
    val metric: String = "cpuLoad",
    val imageName: String = "",
    val imageFit: String = "fit",
    val showLabel: Boolean = true,
    val showUnit: Boolean = true,
    val decimals: Int = 1,
    val chartPoints: Int = 60,
    val gaugeMax: Double = 100.0,
    val label: String = "",
)
