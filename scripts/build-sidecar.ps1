# 发布 C# Sidecar 为 Tauri 所需的平台 sidecar 二进制
# 输出: src-tauri/binaries/fjyxhr-migrate-x86_64-pc-windows-msvc.exe
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot          # FJYXHR_Migrate_Desk
$proj = Join-Path $root "src-dotnet\DataMigrate.Sidecar\DataMigrate.Sidecar.csproj"
$outDir = Join-Path $root "src-tauri\binaries"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "==> dotnet publish (win-x64, self-contained single-file)"
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# Tauri 要求 sidecar 命名为 <name>-<target-triple>(.exe)
$src = Join-Path $outDir "fjyxhr-migrate.exe"
$dst = Join-Path $outDir "fjyxhr-migrate-x86_64-pc-windows-msvc.exe"
if (Test-Path $dst) { Remove-Item $dst -Force }
Copy-Item $src $dst -Force

Write-Host "==> Sidecar 发布完成: $dst"
