package com.tubawinui3.remote.data

import java.net.URLDecoder

const val DEFAULT_PORT = 18765

data class ConnectTarget(
    val host: String,
    val port: Int = DEFAULT_PORT,
    val code: String? = null,
    val name: String? = null,
    val fingerprint: String? = null,
) {
    override fun toString(): String = "ConnectTarget(host=$host, port=$port, code=<redacted>)"
}

/** 解析电脑端二维码 `tubalink://IP:端口?code=配对码&name=电脑名`，同时兼容手输的 `IP`、`IP:端口`。 */
object ConnectLink {
    fun parse(input: String): ConnectTarget? {
        var s = input.trim()
        if (s.isEmpty()) return null
        val query = HashMap<String, String>()
        val qi = s.indexOf('?')
        if (qi >= 0) {
            for (part in s.substring(qi + 1).split('&')) {
                if (part.isEmpty()) continue
                val eq = part.indexOf('=')
                val k = decode(if (eq < 0) part else part.substring(0, eq))
                val v = if (eq < 0) "" else decode(part.substring(eq + 1))
                query[k] = v
            }
            s = s.substring(0, qi)
        }
        s = s.removePrefix("tubalink://").removePrefix("https://").trimEnd('/')
        if (s.isEmpty() || s.contains('/') || s.contains(' ')) return null
        val colon = s.lastIndexOf(':')
        val host: String
        var port = DEFAULT_PORT
        if (colon >= 0) {
            host = s.substring(0, colon)
            port = s.substring(colon + 1).toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        } else host = s
        if (host.isEmpty()) return null
        val fingerprint = query["fp"]?.replace(":", "")?.uppercase()
        if (fingerprint != null && !Regex("[0-9A-F]{64}").matches(fingerprint)) return null
        return ConnectTarget(host, port, query["code"]?.takeIf { it.isNotEmpty() }, query["name"], fingerprint)
    }

    private fun decode(s: String) = try {
        URLDecoder.decode(s, "UTF-8")
    } catch (_: Exception) {
        s
    }
}
