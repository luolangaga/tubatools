import Darwin
import Foundation

public enum ProviderLineTransportError: LocalizedError {
    case invalidArguments(String)
    case invalidSocketPath(String)
    case socketFailure(operation: String, errno: Int32)
    case truncatedRequest
    case invalidUTF8
    case requestTooLarge

    public var errorDescription: String? {
        switch self {
        case let .invalidArguments(message):
            return message
        case let .invalidSocketPath(path):
            return "provider Unix socket path is too long: \(path)"
        case let .socketFailure(operation, errorNumber):
            return "\(operation) failed: \(String(cString: strerror(errorNumber)))"
        case .truncatedRequest:
            return "provider request stream ended before newline"
        case .invalidUTF8:
            return "provider request is not valid UTF-8"
        case .requestTooLarge:
            return "provider request exceeds the 16 MiB wire limit"
        }
    }
}

public struct ProviderLaunchOptions: Equatable {
    public let socketPath: String
    public let pidFile: String

    public init(arguments: [String]) throws {
        var socketPath: String?
        var pidFile: String?
        var index = 1
        while index < arguments.count {
            let flag = arguments[index]
            guard index + 1 < arguments.count else {
                throw ProviderLineTransportError.invalidArguments(
                    "missing value for \(flag)"
                )
            }
            let value = arguments[index + 1]
            switch flag {
            case "--socket":
                socketPath = value
            case "--pid-file":
                pidFile = value
            default:
                throw ProviderLineTransportError.invalidArguments(
                    "unsupported provider argument \(flag)"
                )
            }
            index += 2
        }
        guard let socketPath, let pidFile else {
            throw ProviderLineTransportError.invalidArguments(
                "provider requires --socket and --pid-file"
            )
        }
        self.socketPath = socketPath
        self.pidFile = pidFile
    }
}

public final class ProviderLineTransport {
    static let maximumLineBytes = 16 * 1024 * 1024

    private let inputDescriptor: Int32
    private let outputDescriptor: Int32
    private let ownsDescriptors: Bool
    private var pending = Data()

    public init(arguments: [String]) throws {
        let options = try ProviderLaunchOptions(arguments: arguments)
        try Data(
            "\(ProcessInfo.processInfo.processIdentifier)\n".utf8
        ).write(to: URL(fileURLWithPath: options.pidFile), options: .atomic)
        let descriptor = try Self.connectUnixSocket(path: options.socketPath)
        inputDescriptor = descriptor
        outputDescriptor = descriptor
        ownsDescriptors = true
    }

    init(
        inputDescriptor: Int32,
        outputDescriptor: Int32,
        ownsDescriptors: Bool = false
    ) {
        self.inputDescriptor = inputDescriptor
        self.outputDescriptor = outputDescriptor
        self.ownsDescriptors = ownsDescriptors
    }

    deinit {
        if ownsDescriptors {
            _ = Darwin.close(inputDescriptor)
            if outputDescriptor != inputDescriptor {
                _ = Darwin.close(outputDescriptor)
            }
        }
    }

    public func readLine() throws -> String? {
        while true {
            if let newline = pending.firstIndex(of: 0x0a) {
                var line = pending.prefix(upTo: newline)
                pending.removeSubrange(...newline)
                if line.last == 0x0d {
                    line = line.dropLast()
                }
                guard let decoded = String(data: line, encoding: .utf8) else {
                    throw ProviderLineTransportError.invalidUTF8
                }
                return decoded
            }
            guard pending.count <= Self.maximumLineBytes else {
                throw ProviderLineTransportError.requestTooLarge
            }
            let chunk = try readChunk()
            if chunk.isEmpty {
                if pending.isEmpty {
                    return nil
                }
                throw ProviderLineTransportError.truncatedRequest
            }
            pending.append(chunk)
        }
    }

    public func writeLine(_ data: Data) throws {
        var line = data
        line.append(0x0a)
        try Self.writeAll(line, descriptor: outputDescriptor)
    }

    private func readChunk() throws -> Data {
        var buffer = [UInt8](repeating: 0, count: 4096)
        while true {
            let count = Darwin.read(inputDescriptor, &buffer, buffer.count)
            if count >= 0 {
                return Data(buffer.prefix(Int(count)))
            }
            if errno != EINTR {
                throw ProviderLineTransportError.socketFailure(
                    operation: "read",
                    errno: errno
                )
            }
        }
    }

    private static func writeAll(
        _ data: Data,
        descriptor: Int32
    ) throws {
        try data.withUnsafeBytes { rawBuffer in
            guard let baseAddress = rawBuffer.baseAddress else {
                return
            }
            var offset = 0
            while offset < rawBuffer.count {
                let count = Darwin.write(
                    descriptor,
                    baseAddress.advanced(by: offset),
                    rawBuffer.count - offset
                )
                if count > 0 {
                    offset += count
                } else if count < 0 && errno == EINTR {
                    continue
                } else {
                    throw ProviderLineTransportError.socketFailure(
                        operation: "write",
                        errno: count < 0 ? errno : EIO
                    )
                }
            }
        }
    }

    private static func connectUnixSocket(path: String) throws -> Int32 {
        var address = sockaddr_un()
        address.sun_family = sa_family_t(AF_UNIX)
        let encodedPath = path.utf8CString
        let capacity = MemoryLayout.size(ofValue: address.sun_path)
        guard encodedPath.count <= capacity else {
            throw ProviderLineTransportError.invalidSocketPath(path)
        }
        withUnsafeMutablePointer(to: &address.sun_path) { pointer in
            pointer.withMemoryRebound(to: CChar.self, capacity: capacity) {
                destination in
                encodedPath.withUnsafeBufferPointer { source in
                    if let sourceAddress = source.baseAddress {
                        memcpy(destination, sourceAddress, source.count)
                    }
                }
            }
        }
        let descriptor = Darwin.socket(AF_UNIX, SOCK_STREAM, 0)
        guard descriptor >= 0 else {
            throw ProviderLineTransportError.socketFailure(
                operation: "socket",
                errno: errno
            )
        }
        _ = fcntl(descriptor, F_SETFD, FD_CLOEXEC)
        let result = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                socketAddress in
                Darwin.connect(
                    descriptor,
                    socketAddress,
                    socklen_t(MemoryLayout<sockaddr_un>.size)
                )
            }
        }
        guard result == 0 else {
            let errorNumber = errno
            _ = Darwin.close(descriptor)
            throw ProviderLineTransportError.socketFailure(
                operation: "connect",
                errno: errorNumber
            )
        }
        return descriptor
    }
}
