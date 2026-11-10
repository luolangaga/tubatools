package com.tubawinui3.remote.data

import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.Certificate
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/** Trust is obtained from the PC's QR code, never from unauthenticated LAN discovery. */
class PinnedPcTrust(fingerprint: String?) : X509TrustManager {
    private val pin = fingerprint?.replace(":", "")?.uppercase()?.takeIf { Regex("[0-9A-F]{64}").matches(it) }
        ?.chunked(2)?.map { it.toInt(16).toByte() }?.toByteArray()
    val socketFactory = SSLContext.getInstance("TLS").apply {
        init(null, arrayOf(this@PinnedPcTrust), SecureRandom())
    }.socketFactory

    fun matches(certificate: Certificate?): Boolean = pin != null && certificate != null &&
        MessageDigest.isEqual(pin, MessageDigest.getInstance("SHA-256").digest(certificate.encoded))

    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        val leaf = chain?.firstOrNull() ?: throw CertificateException("电脑证书缺失")
        leaf.checkValidity()
        if (!matches(leaf)) throw CertificateException("电脑身份未验证，请重新扫描电脑上的二维码")
    }
    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        throw CertificateException("不接受客户端证书")
    }
    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}
