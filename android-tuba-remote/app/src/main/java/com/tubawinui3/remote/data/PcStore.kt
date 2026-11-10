package com.tubawinui3.remote.data

import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** 一台记住的电脑。id 由 host:port 派生，同一台电脑重复添加会合并。 */
@Serializable
data class SavedPc(
    val host: String,
    val port: Int = DEFAULT_PORT,
    val name: String = "",
    val token: String? = null,
    val lastConnectedAt: Long = 0,
    val pairingCode: String? = null,
    val fingerprint: String? = null,
) {
    val id: String get() = "$host:$port"
    override fun toString(): String = "SavedPc(host=$host, port=$port, credentials=<redacted>)"
}

/**
 * 多电脑列表的纯逻辑存储：JSON 编解码、合并、删除、旧版单机数据迁移。
 * 与 Android 框架无关，可直接单元测试。
 */
object PcStore {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    fun encode(list: List<SavedPc>): String = json.encodeToString(list)

    fun decode(raw: String?): List<SavedPc> {
        if (raw.isNullOrBlank()) return emptyList()
        return runCatching { json.decodeFromString<List<SavedPc>>(raw) }.getOrDefault(emptyList())
    }

    /** 新增或合并一台电脑（放最前 = 最近使用优先）；已有条目的令牌/名称不会被空值覆盖。 */
    fun upsert(list: List<SavedPc>, pc: SavedPc): List<SavedPc> {
        val old = list.firstOrNull { it.id == pc.id }
        val merged = if (old == null) pc else pc.copy(
            name = pc.name.ifBlank { old.name },
            token = pc.token ?: old.token,
            pairingCode = pc.pairingCode ?: old.pairingCode,
            fingerprint = pc.fingerprint ?: old.fingerprint,
            lastConnectedAt = maxOf(pc.lastConnectedAt, old.lastConnectedAt),
        )
        return listOf(merged) + list.filter { it.id != pc.id }
    }

    fun remove(list: List<SavedPc>, id: String): List<SavedPc> = list.filter { it.id != id }

    fun touch(list: List<SavedPc>, id: String, at: Long): List<SavedPc> =
        list.map { if (it.id == id) it.copy(lastConnectedAt = at) else it }

    /** 旧版单机数据（host/port/token 三个偏好键）迁移为列表。 */
    fun migrate(host: String?, port: Int, token: String?): List<SavedPc> {
        if (host.isNullOrBlank()) return emptyList()
        return listOf(SavedPc(host = host, port = port, token = token))
    }
}
