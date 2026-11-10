@testable import ComputerUseProviderKit
import Darwin
import Foundation
import Testing

@Test
func productionTransportRequiresPrivateSocketAndPIDFile() {
    #expect(throws: ProviderLineTransportError.self) {
        try ProviderLaunchOptions(arguments: ["provider"])
    }
    #expect(throws: ProviderLineTransportError.self) {
        try ProviderLaunchOptions(
            arguments: ["provider", "--socket", "/tmp/provider.sock"]
        )
    }
    let options = try? ProviderLaunchOptions(
        arguments: [
            "provider",
            "--socket", "/tmp/provider.sock",
            "--pid-file", "/tmp/provider.pid",
        ]
    )
    #expect(options?.socketPath == "/tmp/provider.sock")
    #expect(options?.pidFile == "/tmp/provider.pid")
}

@Test
func unknownLaunchArgumentsAreRejected() {
    #expect(throws: ProviderLineTransportError.self) {
        try ProviderLaunchOptions(
            arguments: [
                "provider",
                "--socket", "/tmp/provider.sock",
                "--pid-file", "/tmp/provider.pid",
                "--unexpected", "/tmp/unexpected",
            ]
        )
    }
}

@Test
func newlineTransportDoesNotRequireEOF() throws {
    var inputPipe = [Int32](repeating: -1, count: 2)
    var outputPipe = [Int32](repeating: -1, count: 2)
    #expect(Darwin.pipe(&inputPipe) == 0)
    #expect(Darwin.pipe(&outputPipe) == 0)
    defer {
        for descriptor in inputPipe + outputPipe where descriptor >= 0 {
            _ = Darwin.close(descriptor)
        }
    }

    let transport = ProviderLineTransport(
        inputDescriptor: inputPipe[0],
        outputDescriptor: outputPipe[1]
    )
    let request = Data("{\"request_id\":\"short\"}\n".utf8)
    let written = request.withUnsafeBytes { bytes in
        Darwin.write(inputPipe[1], bytes.baseAddress, bytes.count)
    }
    #expect(written == request.count)
    #expect(try transport.readLine() == "{\"request_id\":\"short\"}")

    try transport.writeLine(Data("{\"ok\":true}".utf8))
    var buffer = [UInt8](repeating: 0, count: 64)
    let read = Darwin.read(outputPipe[0], &buffer, buffer.count)
    #expect(read > 0)
    #expect(
        String(decoding: buffer.prefix(Int(read)), as: UTF8.self)
            == "{\"ok\":true}\n"
    )
}
