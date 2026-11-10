// swift-tools-version: 6.2

import PackageDescription

let package = Package(
    name: "ComputerUseMacProvider",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .library(
            name: "ComputerUseProviderKit",
            type: .dynamic,
            targets: ["ComputerUseProviderKit"]
        ),
        .executable(
            name: "computer-use-macos-provider",
            targets: ["ComputerUseProvider"]
        ),
    ],
    targets: [
        .target(
            name: "ComputerUseProviderKit",
            path: "Sources/ComputerUseProviderKit"
        ),
        .executableTarget(
            name: "ComputerUseProvider",
            dependencies: ["ComputerUseProviderKit"],
            path: "Sources/ComputerUseProvider"
        ),
        .testTarget(
            name: "ComputerUseProviderKitTests",
            dependencies: ["ComputerUseProviderKit"]
        ),
    ]
)
