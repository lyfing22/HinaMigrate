$ErrorActionPreference = 'SilentlyContinue'
Write-Host '--- VC dirs ---'
Get-ChildItem 'C:\Program Files\Microsoft Visual Studio','C:\Program Files (x86)\Microsoft Visual Studio' -Filter VC -Directory -Recurse -Depth 3 | Select-Object -ExpandProperty FullName
Write-Host '--- WindowsSDK Lib ---'
Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Lib' | Select-Object -ExpandProperty Name | Sort-Object -Descending
Write-Host '--- cl.exe on PATH ---'
Get-Command cl.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
Write-Host '--- vcvarsall.bat ---'
Get-ChildItem 'C:\Program Files\Microsoft Visual Studio','C:\Program Files (x86)\Microsoft Visual Studio' -Filter vcvarsall.bat -Recurse -Depth 5 | Select-Object -ExpandProperty FullName
