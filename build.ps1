# Compila o OpenCleaner com o csc.exe que acompanha o .NET Framework 4.x do Windows.
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot 'bin\OpenCleaner.exe'),
    [string[]]$Extra = @(),       # arquivos .cs adicionais (usado pelo teste)
    [string]$Main = '',           # classe com o Main (usado pelo teste)
    [switch]$Console
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe do .NET Framework 4.x nao encontrado.' }

$sources = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) + $Extra

$ico = Join-Path $root 'assets\OpenCleaner.ico'
if (-not (Test-Path $ico)) { & (Join-Path $root 'tools\make-icon.ps1') -Out $ico }

# Regras e ícones Lucide (assets\icons\*.svg) vão embutidos como recursos.
$resources = @("/resource:$root\rules\builtin.ini,OpenCleaner.rules.builtin.ini")
foreach ($svg in Get-ChildItem (Join-Path $root 'assets\icons') -Filter *.svg) {
    $resources += "/resource:$($svg.FullName),OpenCleaner.icons.$($svg.Name)"
}

$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.VisualBasic.dll',
        'Microsoft.CSharp.dll', 'System.Management.dll', 'System.Xml.dll', 'System.Xml.Linq.dll' | ForEach-Object { "/reference:$_" }

$opts = @('/nologo', '/platform:anycpu', '/optimize+', '/codepage:65001', "/out:$Out",
          "/win32manifest:$root\src\app.manifest", "/win32icon:$ico")
$opts += $(if ($Console) { '/target:exe' } else { '/target:winexe' })
if ($Main) { $opts += "/main:$Main" }

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
& $csc @opts @resources @refs @sources
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao ($LASTEXITCODE)" }
Write-Host "OK: $Out"
