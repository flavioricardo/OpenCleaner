# Compila o FreeCleaner com o csc.exe que acompanha o .NET Framework 4.x do Windows.
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe do .NET Framework 4.x nao encontrado.' }

$ico = Join-Path $root 'assets\FreeCleaner.ico'
if (-not (Test-Path $ico)) { & (Join-Path $root 'tools\make-icon.ps1') -Out $ico }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$sources = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$bin\FreeCleaner.exe" `
    "/win32manifest:$root\src\app.manifest" `
    "/win32icon:$ico" `
    "/resource:$root\rules\builtin.ini,FreeCleaner.rules.builtin.ini" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:Microsoft.VisualBasic.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao ($LASTEXITCODE)" }
Write-Host "OK: $bin\FreeCleaner.exe"
