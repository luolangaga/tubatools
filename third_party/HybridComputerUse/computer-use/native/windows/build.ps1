#Requires -Version 7.0

param(
    [ValidateSet("amd64", "arm64")]
    [string]$Architecture = "amd64",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$repositoryRoot = (Resolve-Path (Join-Path $sourceRoot "..")).Path
$Version = (Get-Content -Raw (Join-Path $repositoryRoot "VERSION")).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Repository VERSION must not be empty"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/windows"
}
[void](New-Item -ItemType Directory -Force -Path $OutputDirectory)
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
foreach ($name in @("LICENSE", "THIRD_PARTY_NOTICES.md")) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $name) -Destination $OutputDirectory -Force
}
$licenseDirectory = Join-Path $OutputDirectory "licenses"
[void](New-Item -ItemType Directory -Force -Path $licenseDirectory)
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "licenses") | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licenseDirectory -Recurse -Force
}
$textLicenseDirectory = Join-Path $OutputDirectory "licenses/golang.org/x/text"
[void](New-Item -ItemType Directory -Force -Path $textLicenseDirectory)
foreach ($name in @("LICENSE", "PATENTS")) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "vendor/golang.org/x/text/$name") `
        -Destination $textLicenseDirectory -Force
}
$runtimeIdentifier = if ($Architecture -eq "arm64") {
    "win-arm64"
} else {
    "win-x64"
}
$providerProject = Join-Path `
    $PSScriptRoot `
    "ComputerUse.Windows.Runtime/ComputerUse.Windows.Runtime.csproj"
$providerOutput = Join-Path $OutputDirectory "provider-publish"

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $providerOutput
dotnet publish $providerProject `
    -c Release `
    -r $runtimeIdentifier `
    --self-contained true `
    -p:Version=$Version `
    -o $providerOutput
if ($LASTEXITCODE -ne 0) {
    throw "Windows provider publish failed with exit code $LASTEXITCODE"
}

# Single-file publishing does not carry NuGet license files into the bundle.
$resolvedJson = dotnet msbuild $providerProject `
    -target:ResolveReferences `
    -property:Configuration=Release `
    -property:RuntimeIdentifier=$runtimeIdentifier `
    -getItem:ResolvedRuntimePack `
    -getProperty:ProjectAssetsFile
if ($LASTEXITCODE -ne 0) {
    throw "Windows provider dependency inspection failed with exit code $LASTEXITCODE"
}
$resolved = ($resolvedJson -join "`n") | ConvertFrom-Json
$assets = Get-Content -Raw $resolved.Properties.ProjectAssetsFile | ConvertFrom-Json
$packages = @{}
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne "package") { continue }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $packageDirectory = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $packageDirectory -PathType Container) {
            $packages[$library.Name] = $packageDirectory
            break
        }
    }
    if (-not $packages.ContainsKey($library.Name)) {
        throw "Restored package directory is missing for $($library.Name)"
    }
}
foreach ($pack in $resolved.Items.ResolvedRuntimePack) {
    $packages["$($pack.NuGetPackageId)/$($pack.NuGetPackageVersion)"] = $pack.PackageDirectory
}
if (-not $resolved.Items.ResolvedRuntimePack) {
    throw "Self-contained publish did not resolve any runtime packs"
}
$noticesDirectory = Join-Path $OutputDirectory "licenses/windows-provider"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $noticesDirectory
foreach ($package in $packages.GetEnumerator() | Sort-Object Name) {
    $notices = @(Get-ChildItem -LiteralPath $package.Value -Recurse -File |
        Where-Object { $_.Name -match "^(LICENSE|COPYING|NOTICE|THIRD[-_]PARTY[-_]NOTICES)([.]|$)" })
    if ($notices.Count -eq 0) {
        throw "No license or notice file found for $($package.Name); review its redistribution terms"
    }
    foreach ($notice in $notices) {
        $relativePath = [System.IO.Path]::GetRelativePath($package.Value, $notice.FullName)
        $destination = Join-Path (Join-Path $noticesDirectory $package.Name) $relativePath
        [void](New-Item -ItemType Directory -Force -Path (Split-Path $destination))
        Copy-Item -LiteralPath $notice.FullName -Destination $destination -Force
    }
}

$providerSource = Join-Path `
    $providerOutput `
    "computer-use-windows-provider.exe"
$providerDestination = Join-Path `
    $OutputDirectory `
    "computer-use-windows-provider.exe"
if (-not (Test-Path $providerSource)) {
    throw "Windows provider publish did not produce $providerSource"
}
Move-Item -Force $providerSource $providerDestination
Remove-Item -Recurse -Force $providerOutput

$previousGOOS = $env:GOOS
$previousGOARCH = $env:GOARCH
$previousCGO = $env:CGO_ENABLED
try {
    $env:GOOS = "windows"
    $env:GOARCH = $Architecture
    $env:CGO_ENABLED = "0"
    Push-Location $sourceRoot
    try {
        $ldflags = (
            "-s -w " +
            "-X main.version=$Version"
        )
        go build -trimpath `
            -ldflags $ldflags `
            -o (Join-Path $OutputDirectory "computer-use.exe") `
            ./cmd/computer-use
        if ($LASTEXITCODE -ne 0) {
            throw "Windows MCP build failed with exit code $LASTEXITCODE"
        }
    } finally {
        Pop-Location
    }
} finally {
    $env:GOOS = $previousGOOS
    $env:GOARCH = $previousGOARCH
    $env:CGO_ENABLED = $previousCGO
}

Write-Output "Built Windows runtime: $(Join-Path $OutputDirectory 'computer-use.exe')"
Write-Output "Built Windows provider: $providerDestination"
Write-Output "Windows provider license notices: $noticesDirectory"
