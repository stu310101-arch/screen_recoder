$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'AureoRecorder.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $PSScriptRoot '..\App')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Copy-Item (Join-Path $PSScriptRoot "aureo.ico") (Join-Path $PSScriptRoot "..\App\aureo.ico") -Force
foreach ($name in @("使用說明.md", "使用說明.txt", "THIRD-PARTY-LICENSES.txt")) { Copy-Item (Join-Path $PSScriptRoot "..\$name") (Join-Path $PSScriptRoot "..\App\$name") -Force }
