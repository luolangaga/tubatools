$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path "node_modules")) { npm install }
    npm run build
    Write-Host "Web assets written to plugins/Tubatools.NexBoxSkin/assets/web"
}
finally {
    Pop-Location
}
