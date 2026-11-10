package com.tubawinui3.remote

import com.tubawinui3.remote.data.DashboardConfig
import com.tubawinui3.remote.data.DashboardItem
import com.tubawinui3.remote.data.DashboardStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DashboardStoreTest {

    @Test
    fun defaultConfig_isInsideGrid() {
        val config = DashboardStore.defaultConfig()
        assertTrue(config.items.isNotEmpty())
        config.items.forEach { item ->
            assertTrue("x >= 0", item.x >= 0)
            assertTrue("y >= 0", item.y >= 0)
            assertTrue("w > 0", item.w > 0)
            assertTrue("h > 0", item.h > 0)
            assertTrue("right edge", item.x + item.w <= config.columns)
        }
    }

    @Test
    fun encodeDecode_roundTrip() {
        val config = DashboardConfig(
            columns = 12,
            items = listOf(
                DashboardItem(id = "a", type = "metric", x = 1, y = 2, w = 3, h = 2, metric = "cpuTemp", label = "CPU"),
                DashboardItem(id = "b", type = "text", x = 0, y = 0, w = 6, h = 1, text = "你好", textSize = 20f),
            ),
        )
        assertEquals(config, DashboardStore.decode(DashboardStore.encode(config)))
    }

    @Test
    fun decode_garbage_returnsDefault() {
        assertEquals(DashboardStore.defaultConfig(), DashboardStore.decode("not json"))
        assertEquals(DashboardStore.defaultConfig(), DashboardStore.decode(null))
    }

    @Test
    fun decode_unknownFieldsAndBadValuesAreNormalized() {
        val raw = """{"version":2,"columns":99,"rowHeightDp":999,"items":[
            {"id":"x","type":"futureWidget","x":-5,"y":-2,"w":999,"h":0,"extra":"ignored"}
        ]}"""
        val config = DashboardStore.decode(raw)
        assertEquals(24, config.columns)
        assertTrue(config.rowHeightDp <= 120)
        val item = config.items.single()
        assertEquals(0, item.x)
        assertEquals(0, item.y)
        assertEquals(24, item.w)
        assertEquals(1, item.h)
    }

    @Test
    fun newItem_doesNotOverlapExisting() {
        val config = DashboardStore.defaultConfig()
        val item = DashboardStore.newItem("metric", config)
        assertFalse(config.items.any { overlaps(item, it) })
        assertTrue(item.x >= 0 && item.y >= 0)
        assertTrue(item.x + item.w <= config.columns)
    }

    @Test
    fun autoPack_removesOverlaps() {
        val config = DashboardConfig(
            items = listOf(
                DashboardItem(id = "a", type = "text", x = 0, y = 0, w = 6, h = 2),
                DashboardItem(id = "b", type = "text", x = 2, y = 0, w = 6, h = 2),
                DashboardItem(id = "c", type = "text", x = 1, y = 1, w = 4, h = 2),
            ),
        )
        val packed = DashboardStore.autoPack(config)
        assertEquals(3, packed.items.size)
        for (i in packed.items.indices) {
            for (j in i + 1 until packed.items.size) {
                assertFalse(overlaps(packed.items[i], packed.items[j]))
            }
        }
    }

    @Test
    fun clamp_keepsItemInsideColumns() {
        val item = DashboardItem(id = "x", x = 11, y = -3, w = 8, h = 99)
        val clamped = DashboardStore.clamp(item, 12)
        assertTrue(clamped.x >= 0)
        assertTrue(clamped.x + clamped.w <= 12)
        assertEquals(0, clamped.y)
        assertEquals(12, clamped.h)
    }

    private fun overlaps(a: DashboardItem, b: DashboardItem): Boolean =
        a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y
}
