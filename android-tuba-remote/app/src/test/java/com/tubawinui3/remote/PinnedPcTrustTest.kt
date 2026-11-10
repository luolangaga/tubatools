package com.tubawinui3.remote

import com.tubawinui3.remote.data.ConnectLink
import com.tubawinui3.remote.data.PcStore
import com.tubawinui3.remote.data.PinnedPcTrust
import com.tubawinui3.remote.data.SavedPc
import org.junit.Assert.*
import org.junit.Test
import java.security.MessageDigest
import java.security.PublicKey
import java.security.cert.Certificate
import java.security.cert.CertificateException

class PinnedPcTrustTest {
    private val encodedBytes: ByteArray = byteArrayOf(1, 2, 3, 4)
    private val certificate: Certificate = object : Certificate("X.509") {
        override fun getEncoded(): ByteArray = encodedBytes
        override fun verify(key: PublicKey?) = Unit
        override fun verify(key: PublicKey?, provider: String?) = Unit
        override fun getPublicKey(): PublicKey? = null
        override fun toString() = "Test certificate"
    }
    private val pin = MessageDigest.getInstance("SHA-256").digest(encodedBytes).joinToString("") { "%02X".format(it) }

    @Test fun onlyAnOutOfBandMatchingFingerprintIsAccepted() {
        assertTrue(PinnedPcTrust(pin).matches(certificate))
        assertFalse(PinnedPcTrust("00".repeat(32)).matches(certificate))
        assertFalse(PinnedPcTrust(null).matches(certificate))
        assertFalse(PinnedPcTrust("invalid").matches(certificate))
        assertFalse(PinnedPcTrust(pin).matches(null))
    }

    @Test fun missingCertificateChainIsRejected() {
        try {
            PinnedPcTrust(pin).checkServerTrusted(emptyArray(), "RSA")
            fail("Missing certificates must not be trusted")
        } catch (_: CertificateException) { }
    }

    @Test fun qrCarriesThePinAndMalformedPinsAreRejected() {
        assertEquals(pin, ConnectLink.parse("tubalink://192.168.1.2:18765?code=123456&fp=$pin")!!.fingerprint)
        assertNull(ConnectLink.parse("tubalink://192.168.1.2?fp=invalid"))
        assertNull(ConnectLink.parse("http://192.168.1.2:18765"))
    }

    @Test fun pinSurvivesSavingAndMerging() {
        val pc = SavedPc("192.168.1.2", fingerprint = pin)
        assertEquals(pin, PcStore.decode(PcStore.encode(listOf(pc))).single().fingerprint)
        assertEquals(pin, PcStore.upsert(listOf(pc), pc.copy(fingerprint = null)).single().fingerprint)
    }
}
