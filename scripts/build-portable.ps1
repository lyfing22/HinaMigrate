# 打包「绿色版」：无需 NSIS / WiX，直接产出解压即用的 exe
# 用法: powershell -ExecutionPolicy Bypass -File scripts/build-portable.ps1
$ErrorActionPreference = 'Stop'

# 全部路径由 $PSScriptRoot 派生，避免 .ps1 内硬编码中文路径在 PS 5.1 下被按 GBK 读乱
$desk     = Split-Path -Parent $PSScriptRoot
$release  = Join-Path $desk 'src-tauri\target\release'
$hostExe  = Join-Path $release 'fjyxhr-migrate-desk.exe'
$sideExe  = Join-Path $release 'fjyxhr-migrate.exe'

# 1. release 编译（--no-bundle 跳过安装器，不需要 NSIS / WiX）
Write-Host "==> pnpm tauri build --no-bundle" -ForegroundColor Cyan
Set-Location $desk
& pnpm tauri build --no-bundle
if ($LASTEXITCODE -ne 0) { throw "tauri build 失败" }

foreach ($f in @($hostExe, $sideExe)) {
  if (-not (Test-Path $f)) { throw "产物缺失: $f" }
}

# 2. 收集到绿色版目录
$outName = 'FJYHR_Migrate_Desk'
$outDir  = Join-Path $desk "release-portable\$outName"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item $hostExe $outDir -Force
Copy-Item $sideExe $outDir -Force

# 3. 体积汇总
Write-Host "==> 绿色版输出: $outDir" -ForegroundColor Green
Get-ChildItem $outDir | ForEach-Object {
  Write-Host ("    {0,10:N0} B  {1}" -f $_.Length, $_.Name)
}
$total = (Get-ChildItem $outDir | Measure-Object Length -Sum).Sum
Write-Host ("    {0,10:N0} B  合计 ({1:N2} MB)" -f $total, ($total/1MB))
