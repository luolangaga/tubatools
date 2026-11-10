package com.tubawinui3.remote.data

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.launch

/**
 * 一次摄像头 MJPEG 会话：后台读流，帧放进 conflated Channel（只保留最新帧），UI 侧消费。
 * [stop] 会取消 OkHttp Call，强制打断阻塞读。
 */
class CameraStreamSession(
    private val client: ApiClient,
    private val camera: Int,
    private val width: Int,
    private val fps: Int,
    private val quality: Int,
) {
    val frames = Channel<ByteArray>(Channel.CONFLATED)

    private var call: okhttp3.Call? = null
    private var job: Job? = null

    @Volatile
    private var active = false

    fun start(scope: CoroutineScope, onError: (String) -> Unit) {
        if (active) return
        active = true
        job = scope.launch(Dispatchers.IO) {
            try {
                client.cameraStream(
                    camera = camera,
                    width = width,
                    fps = fps,
                    quality = quality,
                    onCall = { call = it },
                    onFrame = { bytes -> if (active) frames.trySend(bytes) },
                )
            } catch (e: Exception) {
                if (active) onError(e.message ?: "摄像头连接中断")
            } finally {
                active = false
                frames.close()
            }
        }
    }

    fun stop() {
        active = false
        call?.cancel()
        job?.cancel()
        frames.close()
    }
}
