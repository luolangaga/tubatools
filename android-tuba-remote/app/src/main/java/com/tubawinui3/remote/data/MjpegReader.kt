package com.tubawinui3.remote.data

import okio.BufferedSource
import java.io.IOException

/**
 * 解析 `multipart/x-mixed-replace` 的 MJPEG 流。
 * 电脑端每帧都会带 `Content-Length`，因此按长度精确读取，不依赖扫描边界，
 * 避免 JPEG 数据里恰好出现 boundary 字节导致截断。
 */
class MjpegReader(private val source: BufferedSource, boundary: String) {
    private val boundaryLine = "--" + boundary.trim().trim('"')

    /** 读取下一帧 JPEG；流结束返回 null。 */
    fun readFrame(): ByteArray? {
        while (true) {
            val line = source.readUtf8Line() ?: return null
            if (line.isEmpty()) continue
            if (line.startsWith("$boundaryLine--")) return null
            if (line == boundaryLine || line.startsWith(boundaryLine)) break
        }

        var length = -1
        while (true) {
            val line = source.readUtf8Line() ?: return null
            if (line.isEmpty()) break
            val colon = line.indexOf(':')
            if (colon <= 0) continue
            if (line.substring(0, colon).trim().equals("Content-Length", ignoreCase = true)) {
                length = line.substring(colon + 1).trim().toIntOrNull() ?: -1
            }
        }
        if (length <= 0) throw IOException("MJPEG 帧缺少 Content-Length")

        val frame = source.readByteArray(length.toLong())
        // 每帧末尾的 CRLF；下一次读取会跳过空行
        source.readByteArray(2)
        return frame
    }
}
