# Compila e executa o teste de fumaça. Uso: powershell -ExecutionPolicy Bypass -File tests\run-smoke.ps1 [pasta-de-saida]
param([string]$OutDir = (Join-Path $env:TEMP 'OpenCleanerSmoke'), [switch]$Drivers)  # -Drivers tambem consulta o Windows Update (lento)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$exe = Join-Path $OutDir 'SmokeTest.exe'
& (Join-Path $root 'build.ps1') -Out $exe -Extra (Join-Path $PSScriptRoot 'SmokeTest.cs') -Main SmokeTest -Console
$extra = @(); if ($Drivers) { $extra += '--drivers' }
& $exe $OutDir @extra
exit $LASTEXITCODE
