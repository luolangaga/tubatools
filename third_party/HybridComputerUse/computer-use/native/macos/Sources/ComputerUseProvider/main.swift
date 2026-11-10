import ComputerUseProviderKit
import Darwin
import Foundation

@main
struct ComputerUseProviderMain {
    static func main() async {
        do {
            let transport = try ProviderLineTransport(
                arguments: CommandLine.arguments
            )
            let server = ProviderServer(transport: transport)
            await server.run()
            Darwin._exit(EXIT_SUCCESS)
        } catch {
            fputs("computer-use provider startup failed: \(error)\n", stderr)
            exit(1)
        }
    }
}
