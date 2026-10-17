# Gera os arquivos de distribuição em dist\:
#   OpenCleaner-Setup.exe        instalador (por usuário, sem administrador)
#   OpenCleaner-portable.zip     versão portátil
#   SHA256SUMS.txt               somas de verificação
# Os nomes não levam a versão para o link releases/latest/download/... do README não quebrar.
# Uso: powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 1.0.0]
param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

if (-not $Version) {
    $m = Select-String -Path (Join-Path $root 'src\Program.cs') -Pattern 'AssemblyVersion\("(\d+\.\d+\.\d+)'
    $Version = $m.Matches[0].Groups[1].Value
}

# Compila numa pasta própria: não depende de bin (que pode estar aberto).
$app = Join-Path $env:TEMP 'OpenCleaner-build\OpenCleaner.exe'
& (Join-Path $root "build.ps1") -Out $app

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

# Versão do instalador vem do parâmetro (ou do AssemblyVersion do app).
$info = Join-Path $env:TEMP "OpenCleanerBuildInfo.cs"
@"
using System.Reflection;
[assembly: AssemblyTitle("OpenCleaner Setup")]
[assembly: AssemblyProduct("OpenCleaner")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
namespace OpenCleanerSetup { static class BuildInfo { public const string Version = "$Version"; } }
"@ | Set-Content -Path $info -Encoding UTF8

$setup = Join-Path $dist "OpenCleaner-Setup.exe"
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$setup" `
    "/win32icon:$root\assets\OpenCleaner.ico" "/win32manifest:$root\src\app.manifest" `
    "/resource:$app,payload.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:Microsoft.CSharp.dll `
    (Join-Path $PSScriptRoot 'Setup.cs') $info
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador' }
Remove-Item $info

$stage = Join-Path $env:TEMP "OpenCleaner-portable"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item $app, (Join-Path $root 'LICENSE') $stage
$zip = Join-Path $dist "OpenCleaner-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$sums = Get-ChildItem $dist -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
$sums | Set-Content -Path (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Get-ChildItem $dist | ForEach-Object { "{0,-40} {1,12:N0} bytes" -f $_.Name, $_.Length }
