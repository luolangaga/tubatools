#!/usr/bin/env bash

set -euo pipefail

runtime_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_root="$(cd "${runtime_root}/../.." && pwd)"
repo_root="$(cd "${source_root}/.." && pwd)"
output_dir="${1:-${repo_root}/artifacts/macos}"
version="$(tr -d '\r\n' < "${repo_root}/VERSION")"
if [[ -z "${version}" || "${version}" == *[!0-9A-Za-z.+-]* ]]; then
  echo "VERSION must contain a non-empty release version" >&2
  exit 2
fi
build_driver="${COMPUTER_USE_MACOS_BUILD_DRIVER:-swiftc}"
swiftc_bin="${COMPUTER_USE_SWIFTC:-swiftc}"
go_arch="${GOARCH:-$(go env GOHOSTARCH)}"
case "${go_arch}" in
  amd64) swift_arch=x86_64 ;;
  arm64) swift_arch=arm64 ;;
  *) echo "Unsupported macOS architecture: ${go_arch}" >&2; exit 2 ;;
esac
swift_target="${swift_arch}-apple-macosx14.0"
mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd)"
install -m 0644 "${repo_root}/LICENSE" "${output_dir}/LICENSE"
install -m 0644 "${repo_root}/THIRD_PARTY_NOTICES.md" "${output_dir}/THIRD_PARTY_NOTICES.md"
mkdir -p "${output_dir}/licenses"
cp -R "${repo_root}/licenses/." "${output_dir}/licenses/"
text_notices="${output_dir}/licenses/golang.org/x/text"
mkdir -p "${text_notices}"
install -m 0644 "${source_root}/vendor/golang.org/x/text/LICENSE" "${text_notices}/LICENSE"
install -m 0644 "${source_root}/vendor/golang.org/x/text/PATENTS" "${text_notices}/PATENTS"
bundle_name="ComputerUseProvider.app"
bundle_root="${output_dir}/${bundle_name}"
bundle_macos="${bundle_root}/Contents/MacOS"
bundle_frameworks="${bundle_root}/Contents/Frameworks"
bundle_info="${bundle_root}/Contents/Info.plist"
sdk_path="$(
  printf '%s' "${COMPUTER_USE_MACOS_SDK:-$(
    xcrun --sdk macosx --show-sdk-path
  )}"
)"
rm -rf "${bundle_root}"

if [[ "${build_driver}" == "swiftpm" ]]; then
  swift build \
    --package-path "${runtime_root}" \
    --configuration release \
    --triple "${swift_target}"
  bin_path="$(
    swift build \
      --package-path "${runtime_root}" \
      --configuration release \
      --triple "${swift_target}" \
      --show-bin-path
  )"
  install -m 0755 \
    "${bin_path}/computer-use-macos-provider" \
    "${output_dir}/computer-use-macos-provider"
  install -m 0755 \
    "${bin_path}/libComputerUseProviderKit.dylib" \
    "${output_dir}/libComputerUseProviderKit.dylib"
  install_name_tool -add_rpath @executable_path/../Frameworks \
    "${output_dir}/computer-use-macos-provider"
elif [[ "${build_driver}" == "swiftc" ]]; then
  swift_output="${output_dir}/swift-runtime"
  rm -rf "${swift_output}"
  mkdir -p "${swift_output}"
  provider_swift_sources=(
    "${runtime_root}/Sources/ComputerUseProviderKit/AXChangeWake.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AXTextSelection.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityAXReading.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityNodeFacts.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilitySnapshot.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilitySnapshotWindowSelection.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityThread.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityTraversalPath.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityTreeRenderer.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityTreeRenderingSupport.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityTreeSummarySupport.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AccessibilityWindowCapture.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AppDiscovery.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/AppSafetyPolicy.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseRuntimeAccessibility.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseRuntimeBridge.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseRuntimeGUI.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseRuntimeOrderedInput.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/BoundWindowLiveness.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseRuntimeWindows.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseService.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseServiceAccessibilityActions.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseServiceOperations.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseServicePointerInput.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseServiceTextInput.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ComputerUseServiceTextSelection.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/DesktopInputRuntime.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/DesktopRuntime.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/DesktopZoom.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/Errors.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/GUIDrag.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/GUIStillness.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/InputSimulation.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/KeyMapping.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/Permissions.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/PostInputObservation.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ProviderLineTransport.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ProviderObjectIdentity.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ProviderRequestValidator.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ProviderSnapshotProjection.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ProviderServer.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/RelativeReadbackDiagnostic.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/SkyClickSimulation.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/SkyLightSPI.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/ToolResult.swift"
    "${runtime_root}/Sources/ComputerUseProviderKit/WindowActivation.swift"
  )
  "${swiftc_bin}" \
    -O \
    -swift-version 6 \
    -target "${swift_target}" \
    -sdk "${sdk_path}" \
    -num-threads "${COMPUTER_USE_SWIFT_THREADS:-4}" \
    -parse-as-library \
    -emit-library \
    -emit-module \
    -module-name ComputerUseProviderKit \
    "${provider_swift_sources[@]}" \
    -emit-module-path "${swift_output}/ComputerUseProviderKit.swiftmodule" \
    -Xlinker -install_name \
    -Xlinker @rpath/libComputerUseProviderKit.dylib \
    -o "${swift_output}/libComputerUseProviderKit.dylib"
  "${swiftc_bin}" \
    -O \
    -swift-version 6 \
    -target "${swift_target}" \
    -sdk "${sdk_path}" \
    -parse-as-library \
    -I "${swift_output}" \
    -L "${swift_output}" \
    -lComputerUseProviderKit \
    "${runtime_root}/Sources/ComputerUseProvider/main.swift" \
    -Xlinker -rpath \
    -Xlinker @executable_path/../Frameworks \
    -o "${swift_output}/computer-use-macos-provider"
  install -m 0755 \
    "${swift_output}/computer-use-macos-provider" \
    "${output_dir}/computer-use-macos-provider"
  install -m 0755 \
    "${swift_output}/libComputerUseProviderKit.dylib" \
    "${output_dir}/libComputerUseProviderKit.dylib"
  rm -rf "${swift_output}"
else
  echo "Unsupported macOS build driver: ${build_driver}" >&2
  exit 2
fi

codesign_identity="${COMPUTER_USE_CODESIGN_IDENTITY:--}"
codesign --force --sign "${codesign_identity}" \
  "${output_dir}/libComputerUseProviderKit.dylib"
codesign --force --sign "${codesign_identity}" \
  "${output_dir}/computer-use-macos-provider"

mkdir -p "${bundle_macos}" "${bundle_frameworks}"
install -m 0755 \
  "${output_dir}/computer-use-macos-provider" \
  "${bundle_macos}/computer-use-macos-provider"
install -m 0755 \
  "${output_dir}/libComputerUseProviderKit.dylib" \
  "${bundle_frameworks}/libComputerUseProviderKit.dylib"
install -m 0644 "${runtime_root}/ProviderInfo.plist" "${bundle_info}"
plutil -replace CFBundleShortVersionString -string "${version}" "${bundle_info}"
plutil -replace CFBundleVersion -string "${version}" "${bundle_info}"

codesign --force --sign "${codesign_identity}" \
  "${bundle_frameworks}/libComputerUseProviderKit.dylib"
codesign --force --sign "${codesign_identity}" \
  "${bundle_macos}/computer-use-macos-provider"
codesign --force --sign "${codesign_identity}" "${bundle_root}"
codesign --verify --deep --strict "${bundle_root}"

(
  cd "${source_root}"
  CGO_ENABLED=0 GOOS=darwin GOARCH="${go_arch}" go build \
  -trimpath \
  -ldflags "-s -w -X main.version=${version}" \
  -o "${output_dir}/computer-use" \
  ./cmd/computer-use
)
codesign --force --sign "${codesign_identity}" \
  "${output_dir}/computer-use"

echo "Built macOS provider bundle: ${bundle_root}"
echo "Built macOS MCP wrapper: ${output_dir}/computer-use"
