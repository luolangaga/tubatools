package com.tubawinui3.remote

import android.app.Activity
import android.app.Application
import android.os.Bundle
import com.tubawinui3.remote.service.NotificationHelper
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * 应用级状态：前后台可见性（决定后台服务是否弹通知）与通知渠道初始化。
 */
class TubaRemoteApp : Application() {
    override fun onCreate() {
        super.onCreate()
        ApplicationHolder.attach(this)
        registerActivityLifecycleCallbacks(object : ActivityLifecycleCallbacks {
            override fun onActivityStarted(activity: Activity) = AppVisibility.started()
            override fun onActivityStopped(activity: Activity) = AppVisibility.stopped()
            override fun onActivityCreated(activity: Activity, savedInstanceState: Bundle?) = Unit
            override fun onActivityResumed(activity: Activity) = Unit
            override fun onActivityPaused(activity: Activity) = Unit
            override fun onActivitySaveInstanceState(activity: Activity, outState: Bundle) = Unit
            override fun onActivityDestroyed(activity: Activity) = Unit
        })
        NotificationHelper.ensureChannels(this)
        cleanupStaleFiles()
    }

    /** 清理分享缓存与过期下载临时文件，避免长期占用空间。 */
    private fun cleanupStaleFiles() {
        val now = System.currentTimeMillis()
        listOf(
            File(cacheDir, "share") to TimeUnit.HOURS.toMillis(24),
            File(cacheDir, "transfers") to TimeUnit.DAYS.toMillis(7),
        ).forEach { (dir, maxAge) ->
            runCatching {
                dir.listFiles()?.forEach { file ->
                    if (now - file.lastModified() > maxAge) file.deleteRecursively()
                }
            }
        }
    }
}

/** 让 data 层在不持有 Context 的地方也能拿到 Application（下载队列/通知用）。 */
object ApplicationHolder {
    lateinit var app: TubaRemoteApp
        private set

    fun attach(app: TubaRemoteApp) {
        this.app = app
    }
}

/** 前后台可见性：只有 App 不在前台时才弹电脑消息通知。 */
object AppVisibility {
    @Volatile
    var isForeground: Boolean = false
        private set

    private var started = 0

    fun started() {
        started++
        isForeground = started > 0
    }

    fun stopped() {
        started = (started - 1).coerceAtLeast(0)
        isForeground = started > 0
    }
}
