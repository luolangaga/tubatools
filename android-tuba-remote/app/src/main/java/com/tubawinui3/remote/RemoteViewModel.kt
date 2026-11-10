package com.tubawinui3.remote

import android.app.Application
import android.graphics.BitmapFactory
import android.os.Build
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.ChatMessage
import com.tubawinui3.remote.data.ConnectTarget
import com.tubawinui3.remote.data.HardwareSection
import com.tubawinui3.remote.data.PcStore
import com.tubawinui3.remote.data.SavedPc
import com.tubawinui3.remote.data.DiscoveryProtocol
import com.tubawinui3.remote.data.LanDiscovery
import com.tubawinui3.remote.data.SavedReconnect
import com.tubawinui3.remote.service.LinkService
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** 电脑在线状态（「我的电脑」列表展示）。 */
enum class PcStatus { Unknown, Checking, Online, Offline }

class RemoteViewModel(app: Application) : AndroidViewModel(app) {
    private val prefs = app.getSharedPreferences("remote", 0)
    private val pcsPrefKey = "pcs"
    private val lastPcPrefKey = "lastPcId"

    var client by mutableStateOf<ApiClient?>(null)
        private set
    var hostName by mutableStateOf("")
        private set
    var connecting by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    var hardware by mutableStateOf<List<HardwareSection>?>(null)
        private set
    var hardwareError by mutableStateOf<String?>(null)
        private set
    var screenshot by mutableStateOf<ImageBitmap?>(null)
        private set
    var screenshotLoading by mutableStateOf(false)
        private set
    var screenshotError by mutableStateOf<String?>(null)
        private set

    /** 当前连接的电脑（副屏/摄像头/下载页用）。 */
    var currentPcId by mutableStateOf<String?>(null)
        private set
    var currentPcName by mutableStateOf("")
        private set

    /** 通知点击后要打开的工具；连接成功后再消费。 */
    var pendingTool by mutableStateOf<String?>(null)
        private set

    /** 后台接收电脑消息（前台服务）。 */
    var bgEnabled by mutableStateOf(prefs.getBoolean("bgEnabled", true))
        private set

    /** 电脑推送文件时自动下载。 */
    var autoDownload by mutableStateOf(prefs.getBoolean("autoDownload", false))
        private set

    val messages = mutableStateListOf<ChatMessage>()

    /** 已记住的电脑（最近连接优先）。 */
    val devices = mutableStateListOf<SavedPc>()

    /** 每台电脑的在线状态。 */
    val statuses = mutableStateMapOf<String, PcStatus>()

    /** 需要用户输入配对码的目标（未配对 / 配对失效），由列表页弹出配对面板消费。 */
    var pairTarget by mutableStateOf<ConnectTarget?>(null)
        private set

    val connected get() = client != null

    val discovered = mutableStateListOf<ConnectTarget>()
    var discovering by mutableStateOf(false)
        private set
    var discoveryMessage by mutableStateOf<String?>(null)
        private set
    private var discoveryJob: Job? = null
    private var connectionJob: Job? = null
    private var pollJob: Job? = null
    private var clientPcId: String? = null

    init {
        var list = PcStore.decode(prefs.getString(pcsPrefKey, null))
        if (list.isEmpty()) {
            // 旧版单机数据迁移：host/port/token → 单条目
            val legacy = PcStore.migrate(
                prefs.getString("host", null),
                prefs.getInt("port", 18765),
                prefs.getString("token", null),
            )
            if (legacy.isNotEmpty()) {
                list = legacy
                prefs.edit()
                    .putString(pcsPrefKey, PcStore.encode(list))
                    .putString(lastPcPrefKey, legacy.first().id)
                    .remove("host")
                    .remove("port")
                    .remove("token")
                    .apply()
            }
        }
        devices.addAll(list)
    }

    // ───────── 设备列表 ─────────

    private fun saveDevices(list: List<SavedPc>) {
        devices.clear()
        devices.addAll(list)
        prefs.edit().putString(pcsPrefKey, PcStore.encode(list)).apply()
    }

    fun removeDevice(id: String) {
        connectionJob?.cancel()
        connecting = false
        pairTarget = null
        if (clientPcId == id) disconnect()
        if (currentPcId == id) {
            LinkService.stop(getApplication())
            currentPcId = null
            currentPcName = ""
        }
        saveDevices(PcStore.remove(devices, id))
        statuses.remove(id)
        if (prefs.getString(lastPcPrefKey, null) == id) prefs.edit().remove(lastPcPrefKey).apply()
    }

    /** 仅用无鉴权 ping 探测在线状态，不让它改写已保存的设备名称。 */
    fun refreshStatuses() {
        val list = devices.toList()
        for (pc in list) {
            if (statuses[pc.id] == PcStatus.Checking) continue
            if (statuses[pc.id] == null) statuses[pc.id] = PcStatus.Checking
            viewModelScope.launch {
                val name = runCatching { ApiClient(pc.host, pc.port, fingerprint = pc.fingerprint).ping() }.getOrNull()
                if (devices.none { it.id == pc.id }) return@launch
                statuses[pc.id] = if (name != null) PcStatus.Online else PcStatus.Offline

            }
        }
    }

    // ───────── 连接 ─────────

    /** 启动时仅尝试上次成功连接的地址；发现名称不构成迁移凭据的依据。 */
    fun tryRestore() {
        val lastId = prefs.getString(lastPcPrefKey, null) ?: return
        devices.firstOrNull { it.id == lastId }?.let { requestConnect(it) }
    }

    fun requestConnect(pc: SavedPc) {
        if (connecting) return
        if (pc.fingerprint == null) {
            error = "安全连接已升级，请扫描电脑上的新二维码重新配对"
            pairTarget = ConnectTarget(pc.host, pc.port, name = pc.name)
            return
        }
        connecting = true
        error = null
        connectionJob = viewModelScope.launch {
            val c = ApiClient(pc.host, pc.port, fingerprint = pc.fingerprint)
            var pairingAttempted = false
            try {
                val token = SavedReconnect.token(pc,
                    authenticate = { c.token = it; c.info() },
                    pair = {
                        pairingAttempted = true
                        updateToken(pc.id, null)
                        c.token = null
                        c.pair(it, "${Build.MANUFACTURER} ${Build.MODEL}")
                        c.token!!
                    },
                )
                if (token == null) {
                    updateToken(pc.id, null)
                    pairTarget = ConnectTarget(pc.host, pc.port, name = pc.name.ifEmpty { null }, fingerprint = pc.fingerprint)
                    return@launch
                }
                c.token = token
                val updated = pc.copy(token = token)
                saveDevices(PcStore.upsert(devices, updated))
                hostName = pc.name.ifEmpty { pc.host }
                onConnected(c, updated)
            } catch (e: ApiException) {
                if (e.code == 401 || (pairingAttempted && e.code in listOf(400, 403))) {
                    saveDevices(devices.map { if (it.id == pc.id) it.copy(token = null, pairingCode = null) else it })
                    pairTarget = ConnectTarget(pc.host, pc.port, name = pc.name.ifEmpty { null }, fingerprint = pc.fingerprint)
                    error = "配对已失效，请输入电脑上的新配对码"
                } else {
                    error = "暂时无法连接电脑，请检查网络和电脑服务后重试；也可手动输入地址与连接码。"
                }
            } finally {
                connecting = false
            }
        }
    }

    fun selectDiscovered(target: ConnectTarget) {
        if (connecting) return
        val saved = DiscoveryProtocol.savedAt(target, devices)
        if (saved != null) requestConnect(saved)
        else {
            error = null
            pairTarget = target
        }
    }

    fun startDiscovery() {
        if (discoveryJob?.isActive == true) return
        discovering = true
        discoveryMessage = null
        discovered.clear()
        discoveryJob = viewModelScope.launch {
            try {
                discovered.addAll(LanDiscovery.discover())
                discoveryMessage = if (discovered.isEmpty()) "未发现电脑，可重试或手动输入地址与连接码。" else "选择电脑后输入电脑上显示的配对码。"
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                discoveryMessage = "局域网发现不可用，请用地址与连接码手动连接。"
            } finally {
                discovering = false
            }
        }
    }

    fun cancelDiscovery() {
        discoveryJob?.cancel()
        discoveryMessage = "已停止发现，可手动输入地址与连接码。"
    }

    fun clearPairTarget() {
        pairTarget = null
    }

    /** 用配对码连接（添加新电脑 / 重新配对都走这里）。 */
    fun connect(target: ConnectTarget, code: String) {
        if (connecting) return
        if (target.fingerprint == null) { error = "请扫码连接，或粘贴电脑上的完整连接信息以验证电脑身份"; return }
        if (!Regex("[0-9]{6}").matches(code.trim())) { error = "请输入电脑上显示的 6 位配对码"; return }
        connecting = true
        error = null
        connectionJob = viewModelScope.launch {
            val c = ApiClient(target.host, target.port, fingerprint = target.fingerprint)
            try {
                val name = c.pair(code.trim(), "${Build.MANUFACTURER} ${Build.MODEL}")
                val pc = SavedPc(
                    host = target.host,
                    port = target.port,
                    name = name,
                    token = c.token,
                    pairingCode = code.trim(),
                    fingerprint = target.fingerprint,
                    lastConnectedAt = System.currentTimeMillis(),
                )
                saveDevices(PcStore.upsert(devices, pc))
                hostName = pc.name
                onConnected(c, pc)
                pairTarget = null
            } catch (e: ApiException) {
                error = "配对失败，请检查地址、连接码及电脑端授权后重试。"
            } finally {
                connecting = false
            }
        }
    }

    fun scanError(msg: String) {
        error = msg
    }

    /** 通知点击导航；连接成功后再打开对应工具。 */
    fun navigateTo(tool: String) {
        pendingTool = tool.uppercase()
    }

    fun consumePendingTool() {
        pendingTool = null
    }

    fun updateBgEnabled(enabled: Boolean) {
        if (bgEnabled == enabled) return
        bgEnabled = enabled
        prefs.edit().putBoolean("bgEnabled", enabled).apply()
        val id = currentPcId
        if (enabled && id != null) {
            runCatching { LinkService.start(getApplication(), id) }
        } else if (!enabled) {
            LinkService.stop(getApplication())
        }
    }

    fun updateAutoDownload(enabled: Boolean) {
        if (autoDownload == enabled) return
        autoDownload = enabled
        prefs.edit().putBoolean("autoDownload", enabled).apply()
    }

    /** 断开当前连接（回到列表）：保留条目与令牌，再次连接无需重新配对。 */
    fun disconnect() {
        pollJob?.cancel()
        LinkService.stop(getApplication())
        client = null
        clientPcId = null
        currentPcId = null
        currentPcName = ""
        hardware = null
        screenshot = null
        messages.clear()
    }

    private fun updateToken(id: String, token: String?) {
        saveDevices(devices.map { if (it.id == id) it.copy(token = token) else it })
    }

    private fun onConnected(c: ApiClient, pc: SavedPc) {
        client = c
        clientPcId = pc.id
        currentPcId = pc.id
        currentPcName = pc.name.ifBlank { pc.host }
        error = null
        messages.clear()
        prefs.edit().putString(lastPcPrefKey, pc.id).apply()
        saveDevices(PcStore.touch(devices, pc.id, System.currentTimeMillis()))
        if (bgEnabled) runCatching { LinkService.start(getApplication(), pc.id) }
        refreshScreenshot()
        loadHardware()
        pollJob?.cancel()
        pollJob = viewModelScope.launch {
            var failures = 0
            while (isActive) {
                try {
                    pollChat(c)
                    failures = 0
                } catch (e: ApiException) {
                    if (e.code == 401 || ++failures >= 4) {
                        error = if (e.code == 401) "连接已失效，请重新配对" else "与电脑的连接已中断"
                        disconnectKeepTarget()
                        return@launch
                    }
                }
                delay(2000)
            }
        }
    }

    private fun disconnectKeepTarget() {
        client = null
        clientPcId = null
        hardware = null
        screenshot = null
    }

    private suspend fun pollChat(c: ApiClient) {
        val last = messages.lastOrNull()?.seq ?: 0
        val fresh = c.chatList(last)
        if (fresh.isNotEmpty()) {
            messages.addAll(fresh.filter { m -> messages.none { it.seq == m.seq } })
            // 让后台服务的游标跟上 UI，避免重新启动服务时补发已看过的消息
            val latest = messages.lastOrNull()?.seq ?: last
            val stored = prefs.getLong("bgCursor", 0L)
            if (latest > stored) prefs.edit().putLong("bgCursor", latest).apply()
        }
    }

    fun addMessage(m: ChatMessage) {
        if (messages.none { it.seq == m.seq }) messages.add(m)
    }

    fun loadHardware() {
        val c = client ?: return
        viewModelScope.launch {
            try {
                hardware = c.hardware()
                hardwareError = null
            } catch (e: ApiException) {
                hardwareError = e.message
            }
        }
    }

    fun refreshScreenshot() {
        val c = client ?: return
        if (screenshotLoading) return
        screenshotLoading = true
        viewModelScope.launch {
            try {
                val bytes = c.screenshot()
                screenshot = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
                screenshotError = null
            } catch (e: ApiException) {
                screenshotError = e.message
            } finally {
                screenshotLoading = false
            }
        }
    }
}
