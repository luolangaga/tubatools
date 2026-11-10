$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$exe = $env:TUBA_TOOLBOX_EXE
if (-not $exe) { $exe = (ConvertFrom-Json ([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../toolbox.json')))).executable }
if (-not [IO.Path]::IsPathRooted($exe) -or -not [IO.File]::Exists($exe)) { throw 'Toolbox executable not found. Re-export the skill from the toolbox.' }
$folder = Join-Path ([IO.Path]::GetTempPath()) ('tuba-hardware-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
$output = Join-Path $folder 'hardware.json'
try {
 $start = New-Object Diagnostics.ProcessStartInfo
 $start.FileName = $exe; $start.Arguments = '--agent-cli hardware --output "' + $output + '"'
 $start.UseShellExecute = $false; $start.CreateNoWindow = $true
 $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
 $process = [Diagnostics.Process]::Start($start)
 try {
  $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
  if(-not $process.WaitForExit(90000)){$process.Kill();throw 'Hardware query timed out.'}
  if(-not [IO.File]::Exists($output)){throw 'Hardware query did not return a result.'}
  $json=[IO.File]::ReadAllText($output);$result=ConvertFrom-Json $json
  [Console]::Out.WriteLine($json)
  if(-not $result.ok){exit 1}
 }finally{$process.Dispose()}
}finally{
 if([IO.File]::Exists($output)){[IO.File]::Delete($output)}
 [IO.Directory]::Delete($folder)
}
