# Testes que exigem administrador: troca real do tipo de inicialização de serviço (página Jogos).
# Cria o serviço de teste OpenCleanerTestSvc (desativado, apontando para um executável que não existe,
# nunca iniciado), roda os testes e remove o serviço no final, mesmo se algo falhar.
# Uso, num PowerShell aberto como administrador:
#   powershell -ExecutionPolicy Bypass -File tests\run-admin-tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Abra o PowerShell como administrador para rodar estes testes.' -ForegroundColor Yellow; exit 1 }

$name = 'OpenCleanerTestSvc'
$sc = Join-Path $env:WINDIR 'System32\sc.exe'
if (Get-Service $name -ErrorAction SilentlyContinue) { throw "O serviço $name já existe; remova com: sc.exe delete $name" }

$out = Join-Path $env:TEMP 'OpenCleanerAdmin'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'AdminTests.exe'
& (Join-Path $root 'build.ps1') -Out $exe -Extra (Join-Path $PSScriptRoot 'AdminTests.cs') -Main AdminTests -Console
& $sc create $name binPath= '"C:\OpenCleanerTest\nao-existe.exe"' start= disabled DisplayName= 'OpenCleaner Test (temporario)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create falhou ($LASTEXITCODE)" }
try {
    & $exe
    $code = $LASTEXITCODE
}
finally {
    & $sc delete $name | Out-Null
    Start-Sleep 1
    Write-Host ("  serviço de teste removido: " + $(if (Get-Service $name -ErrorAction SilentlyContinue) { 'NÃO (rode sc.exe delete ' + $name + ')' } else { 'sim' }))
}
exit $code
