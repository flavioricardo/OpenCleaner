# Compila e executa o teste de fumaça. Uso: powershell -ExecutionPolicy Bypass -File tests\run-smoke.ps1 [pasta-de-saida]
param([string]$OutDir = (Join-Path $env:TEMP 'FreeCleanerSmoke'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$sources = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) + (Join-Path $PSScriptRoot 'SmokeTest.cs')
$exe = Join-Path $OutDir 'SmokeTest.exe'
& $csc /nologo /target:exe /platform:anycpu /codepage:65001 /main:SmokeTest "/out:$exe" `
    "/resource:$root\rules\builtin.ini,FreeCleaner.rules.builtin.ini" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do teste' }
& $exe $OutDir
exit $LASTEXITCODE
