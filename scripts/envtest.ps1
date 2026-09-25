$desk = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $desk 'src-tauri\target\debug'
$envDir = 'D:\tmp\envtest'
New-Item -ItemType Directory -Force -Path $envDir | Out-Null
$env:FJYXHR_LOG_DIR = $envDir
Write-Host "FJYXHR_LOG_DIR set to: $env:FJYXHR_LOG_DIR"
Set-Location $bin
$inp = '{"id":"1","cmd":"ping"}'
$inp | ./fjyxhr-migrate.exe 2>&1 | Select-Object -First 2
Write-Host "--- envDir contents ---"
Get-ChildItem $envDir | Select-Object Name,Length
