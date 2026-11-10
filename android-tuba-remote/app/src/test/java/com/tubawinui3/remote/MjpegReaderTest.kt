package com.tubawinui3.remote

import com.tubawinui3.remote.data.MjpegReader
import okio.Buffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.IOException

class MjpegReaderTest {

    @Test
    fun readsFramesByContentLength() {
        val a = byteArrayOf(1, 2, 3, 4)
        val b = byteArrayOf(5, 6, 7, 8, 9)
        val buffer = Buffer()
        buffer.writeUtf8(partHeader(a.size))
        buffer.write(a)
        buffer.writeUtf8("\r\n")
        buffer.writeUtf8(partHeader(b.size))
        buffer.write(b)
        buffer.writeUtf8("\r\n--frame--\r\n")

        val reader = MjpegReader(buffer, "frame")
        assertArrayEquals(a, reader.readFrame())
        assertArrayEquals(b, reader.readFrame())
        assertNull(reader.readFrame())
    }

    @Test
    fun payloadContainingBoundaryTextIsNotTruncated() {
        val payload = "--frame\r\nContent-Length: 3\r\n\r\n".toByteArray()
        val buffer = Buffer()
        buffer.writeUtf8(partHeader(payload.size))
        buffer.write(payload)
        buffer.writeUtf8("\r\n")
        buffer.writeUtf8("--frame--\r\n")

        val reader = MjpegReader(buffer, "frame")
        assertArrayEquals(payload, reader.readFrame())
    }

    @Test
    fun quotedBoundaryIsAccepted() {
        val buffer = Buffer()
        buffer.writeUtf8(partHeader(2))
        buffer.write(byteArrayOf(7, 8))
        buffer.writeUtf8("\r\n--frame--\r\n")
        val reader = MjpegReader(buffer, "\"frame\"")
        assertArrayEquals(byteArrayOf(7, 8), reader.readFrame())
    }

    @Test(expected = IOException::class)
    fun missingContentLengthThrows() {
        val buffer = Buffer()
        buffer.writeUtf8("--frame\r\nContent-Type: image/jpeg\r\n\r\nabcd\r\n--frame--\r\n")
        MjpegReader(buffer, "frame").readFrame()
    }

    @Test
    fun emptyStreamReturnsNull() {
        assertNull(MjpegReader(Buffer(), "frame").readFrame())
    }

    private fun partHeader(length: Int): String =
        "--frame\r\nContent-Type: image/jpeg\r\nContent-Length: $length\r\n\r\n"
}
