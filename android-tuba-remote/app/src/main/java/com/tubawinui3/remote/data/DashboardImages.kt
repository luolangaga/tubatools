package com.tubawinui3.remote.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import java.io.File
import java.util.UUID

/** 副屏图片：导入时压到最长边 2048 的 JPEG，文件放在 filesDir，配置里只存文件名。 */
object DashboardImages {
    private const val MaxEdge = 2048

    fun dir(context: Context, pcId: String): File =
        File(File(File(context.filesDir, "dashboard"), safePcId(pcId)), "images").apply { mkdirs() }

    fun import(context: Context, pcId: String, uri: Uri): String? = runCatching {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

        var sample = 1
        while (bounds.outWidth / sample > MaxEdge || bounds.outHeight / sample > MaxEdge) sample *= 2

        val options = BitmapFactory.Options().apply { inSampleSize = sample }
        val bitmap = context.contentResolver.openInputStream(uri)
            ?.use { BitmapFactory.decodeStream(it, null, options) } ?: return null

        val name = UUID.randomUUID().toString().replace("-", "") + ".jpg"
        File(dir(context, pcId), name).outputStream().use { bitmap.compress(Bitmap.CompressFormat.JPEG, 85, it) }
        bitmap.recycle()
        name
    }.getOrNull()

    fun load(context: Context, pcId: String, name: String): Bitmap? {
        if (name.isBlank()) return null
        val file = File(dir(context, pcId), name)
        if (!file.isFile) return null
        return runCatching { BitmapFactory.decodeFile(file.absolutePath) }.getOrNull()
    }

    /** 删除当前布局不再引用的图片。 */
    fun gc(context: Context, pcId: String, config: DashboardConfig) {
        val used = config.items.mapNotNull { it.imageName.takeIf(String::isNotBlank) }.toSet()
        runCatching {
            dir(context, pcId).listFiles()?.forEach { file -> if (file.name !in used) file.delete() }
        }
    }

    private fun safePcId(pcId: String): String =
        pcId.ifBlank { "default" }.replace(Regex("[^A-Za-z0-9._-]"), "_")
}
