# Gera assets\OpenCleaner.ico a partir do icone Lucide "brush-cleaning" (ver tools\MakeIcon.cs).
# Uso: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Out caminho.ico] [-Preview previa.png]
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\assets\OpenCleaner.ico'),
    [string]$Preview = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $env:TEMP 'OpenCleaner-MakeIcon.exe'
$resources = foreach ($svg in Get-ChildItem (Join-Path $root 'assets\icons') -Filter *.svg) {
    "/resource:$($svg.FullName),OpenCleaner.icons.$($svg.Name)"
}
& $csc /nologo /target:exe /codepage:65001 "/out:$exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Core.dll @resources `
    (Join-Path $root 'src\UI\Lucide.cs') (Join-Path $PSScriptRoot 'MakeIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o gerador de icone' }
if ($Preview) { & $exe $Out $Preview } else { & $exe $Out }
Remove-Item $exe -ErrorAction SilentlyContinue
