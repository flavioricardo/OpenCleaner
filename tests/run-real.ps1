# Testes reais: funcionais (sandbox), de segurança e de desempenho.
# Uso: powershell -ExecutionPolicy Bypass -File tests\run-real.ps1 [pasta-de-saida] [-Drivers] [-Quick]
#   -Drivers  também consulta o Windows Update (cerca de 1 minuto)
#   -Quick    pula a análise do disco C: inteiro
# Nada fora da sandbox é apagado; entradas de registro de teste (OpenCleanerTest) são removidas no final.
param([string]$OutDir = (Join-Path $env:TEMP 'OpenCleanerReal'), [switch]$Drivers, [switch]$Quick)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
New-Item -ItemType Directory -Force $OutDir | Out-Null
# O app compilado é usado nos testes de /AUTO e de abertura da janela.
& (Join-Path $root 'build.ps1') -Out (Join-Path $env:TEMP 'OpenCleaner-build\OpenCleaner.exe')
$exe = Join-Path $OutDir 'RealTests.exe'
& (Join-Path $root 'build.ps1') -Out $exe -Extra (Join-Path $PSScriptRoot 'RealTests.cs') -Main RealTests -Console
$extra = @()
if ($Drivers) { $extra += '--drivers' }
if ($Quick) { $extra += '--quick' }
& $exe $OutDir @extra
exit $LASTEXITCODE
