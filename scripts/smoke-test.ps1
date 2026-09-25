$ErrorActionPreference = 'Continue'
# 用 $PSScriptRoot 派生路径,避免脚本内硬编码中文路径在 PS 5.1 下的编码问题
$desk = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $desk 'src-tauri\target\debug\fjyxhr-migrate-desk.exe'
if (-not (Test-Path $exe)) { Write-Host "EXE NOT FOUND: $exe"; exit 2 }

Write-Host "Launching host: $exe"
$proc = Start-Process -FilePath $exe -WorkingDirectory (Join-Path $desk 'src-tauri\target\debug') -PassThru
Start-Sleep -Seconds 18

$hostAlive = -not $proc.HasExited
$sidecar   = Get-Process -Name fjyxhr-migrate -ErrorAction SilentlyContinue

if (-not $hostAlive) { Write-Host "FAIL: host exited early, code=$($proc.ExitCode)" }
else { Write-Host "OK: host alive, PID=$($proc.Id)" }

if ($sidecar) { foreach ($s in $sidecar) { Write-Host "OK: SIDECAR alive, PID=$($s.Id), mem=$([int]($s.WorkingSet64/1MB))MB" } }
else { Write-Host "FAIL: SIDECAR not detected" }

Get-Process -Name fjyxhr-migrate -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Write-Host 'cleaned up'
