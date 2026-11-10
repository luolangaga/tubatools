param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string[]]$Arguments = @(),
    [switch]$ResolveOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)

$exe = $env:TUBA_TOOLBOX_EXE
if (-not $exe) {
    $config = Join-Path $PSScriptRoot '../toolbox.json'
    if (-not [IO.File]::Exists($config)) { throw 'toolbox.json not found. Re-open the toolbox workspace.' }
    $exe = (ConvertFrom-Json ([IO.File]::ReadAllText($config))).executable
}
if (-not [IO.Path]::IsPathRooted($exe) -or -not [IO.File]::Exists($exe)) {
    throw 'Toolbox executable not found. Ask the user to open the toolbox once.'
}

$toolsRoot = Join-Path (Split-Path $exe -Parent) 'Tools'
if (-not [IO.Directory]::Exists($toolsRoot)) { throw "Tools directory not found: $toolsRoot" }

$extensions = @('.exe', '.bat', '.cmd', '.lnk')
$blocked = '(?i)(unins|uninstall|setup|installer|update|crashpad|redist|vc_redist|dotnet)'
$matches = Get-ChildItem -LiteralPath $toolsRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
    Where-Object { $_.FullName -notmatch $blocked } |
    Where-Object { $_.Name -like "*$Name*" -or $_.Directory.Name -like "*$Name*" }

if (-not $matches) {
    [pscustomobject]@{ ok = $false; error = "No installed tool matched '$Name' under $toolsRoot"; candidates = @() } | ConvertTo-Json -Compress
    exit 1
}

# Prefer the native architecture, then architecture-neutral, then 32-bit wrappers.
$ranked = $matches | Sort-Object `
    @{ Expression = { if ($_.FullName -match '(?i)(x64|win64|64bit)') { 0 } elseif ($_.FullName -match '(?i)(x86|win32|32bit)') { 2 } else { 1 } } }, `
    @{ Expression = { $_.FullName.Length } }
$tool = $ranked | Select-Object -First 1
$file = $tool.FullName
$directory = Split-Path $file -Parent

if ($ResolveOnly) {
    [pscustomobject]@{
        ok = $true
        name = $tool.Name
        path = $file
        workingDirectory = $directory
        candidates = @($ranked | ForEach-Object { $_.FullName })
    } | ConvertTo-Json -Compress
    exit 0
}

$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $file
$start.WorkingDirectory = $directory
$start.UseShellExecute = $true
if ($Arguments.Count -gt 0) {
    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }
    $start.Arguments = $quoted -join ' '
}
$process = [Diagnostics.Process]::Start($start)

[pscustomobject]@{
    ok = $true
    name = $tool.Name
    path = $file
    workingDirectory = $directory
    pid = $process.Id
    candidates = @($ranked | ForEach-Object { $_.FullName })
} | ConvertTo-Json -Compress
