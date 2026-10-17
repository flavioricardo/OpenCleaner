# Testes reais do instalador: instalar, atualizar com o app aberto, desinstalar preservando dados e
# DLL plantada ao lado do instalador e do app. Instala de verdade em %LocalAppData%\Programs\OpenCleaner
# e desinstala no final; as configurações do usuário (%AppData%\OpenCleaner) são guardadas e devolvidas.
# Uso: powershell -ExecutionPolicy Bypass -File tests\run-installer-tests.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fail = 0; $ok = 0
function Check($cond, $what) { if ($cond) { $script:ok++; Write-Host "  [ok]    $what" } else { $script:fail++; Write-Host "  [FALHA] $what" -ForegroundColor Red } }

$dir = Join-Path $env:LOCALAPPDATA 'Programs\OpenCleaner'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenCleaner'
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OpenCleaner.lnk'
$data = Join-Path $env:APPDATA 'OpenCleaner'
$sandbox = Join-Path $env:TEMP ("OCInst_" + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory $sandbox | Out-Null

if (Test-Path $dir) { throw "O OpenCleaner já está instalado neste PC; desinstale antes de rodar este teste." }
$dataBackup = Join-Path $sandbox 'dados-do-usuario'
$hadData = Test-Path $data
if ($hadData) { Copy-Item $data $dataBackup -Recurse }

try {
    Write-Host "`n== Compilar instalador =="
    & (Join-Path $PSScriptRoot '..\installer\build-installer.ps1') | Out-Null
    $setup = Join-Path $root 'dist\OpenCleaner-Setup.exe'
    $payloadHash = (Get-FileHash (Join-Path $env:TEMP 'OpenCleaner-build\OpenCleaner.exe')).Hash

    Write-Host "`n== Instalação silenciosa =="
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $setup -ArgumentList '/S', '/LANG=pt' -PassThru -Wait
    Check ($p.ExitCode -eq 0 -and (Test-Path "$dir\OpenCleaner.exe") -and (Test-Path $lnk) -and (Test-Path $key)) "instala sem administrador em $($sw.ElapsedMilliseconds) ms: exe, atalho e entrada em Apps"
    Check ((Get-FileHash "$dir\OpenCleaner.exe").Hash -eq $payloadHash) "exe instalado é idêntico ao compilado"
    $acl = (Get-Acl $dir).Access | Where-Object { $_.IdentityReference -match 'Everyone|Todos|Users|Usuários' -and $_.FileSystemRights -match 'Write|Modify|FullControl' }
    Check (-not $acl) "pasta de instalação não é gravável por outros usuários (só o dono, SYSTEM e administradores)"

    Write-Host "`n== Atualizar com o app aberto =="
    # Data antiga antes de abrir (com o app aberto o arquivo fica travado)
    (Get-Item "$dir\OpenCleaner.exe").LastWriteTime = (Get-Date).AddDays(-1)
    $app = Start-Process "$dir\OpenCleaner.exe" -PassThru
    $app.WaitForInputIdle(15000) | Out-Null
    Start-Sleep 1
    $p = Start-Process $setup -ArgumentList '/S' -PassThru -Wait
    $app.Refresh()
    Check ($p.ExitCode -eq 0 -and $app.HasExited) "o instalador fecha o app aberto antes de substituir"
    Check ((Get-Item "$dir\OpenCleaner.exe").LastWriteTime -gt (Get-Date).AddMinutes(-5)) "exe foi substituído pela versão nova"
    Check ((Get-ItemProperty $key).Language -eq 'pt') "idioma escolhido na primeira instalação é mantido na atualização"

    Write-Host "`n== DLL plantada ao lado do instalador e do app =="
    $plant = Join-Path $sandbox 'downloads'
    New-Item -ItemType Directory $plant | Out-Null
    $dlls = 'version.dll', 'uxtheme.dll', 'dwmapi.dll', 'cryptbase.dll', 'profapi.dll', 'wtsapi32.dll', 'userenv.dll', 'propsys.dll',
            'winmm.dll', 'iphlpapi.dll', 'dnsapi.dll', 'secur32.dll', 'sspicli.dll', 'textshaping.dll', 'netapi32.dll', 'mscms.dll', 'wininet.dll', 'urlmon.dll'
    foreach ($d in $dlls) { $src = Join-Path $env:WINDIR "System32\$d"; if (Test-Path $src) { Copy-Item $src $plant } }
    Copy-Item $setup $plant
    Copy-Item "$dir\OpenCleaner.exe" $plant
    foreach ($exe in 'OpenCleaner-Setup.exe', 'OpenCleaner.exe') {
        $proc = Start-Process (Join-Path $plant $exe) -ArgumentList '/LANG=pt' -PassThru -WorkingDirectory $plant
        try { $proc.WaitForInputIdle(15000) | Out-Null } catch { }
        Start-Sleep 2
        $fromPlant = @(Get-Process -Id $proc.Id -Module -ErrorAction SilentlyContinue | Where-Object { $_.FileName -like "$plant*" -and $_.ModuleName -notlike '*.exe' })
        Check ($fromPlant.Count -eq 0) ("$exe aberto da pasta com " + (Get-ChildItem $plant -Filter *.dll).Count + " DLLs plantadas não carregou nenhuma delas" + $(if ($fromPlant.Count) { ": " + ($fromPlant.ModuleName -join ', ') } else { '' }))
        $proc.CloseMainWindow() | Out-Null; Start-Sleep 1; if (-not $proc.HasExited) { $proc.Kill() }
    }

    Write-Host "`n== Desinstalação silenciosa preserva as configurações =="
    New-Item -ItemType Directory -Force $data | Out-Null
    Add-Content (Join-Path $data 'OpenCleaner.ini') "`r`n[Exclude]`r`nF0=C:\OpenCleanerTestManter"
    $p = Start-Process "$dir\uninstall.exe" -ArgumentList '/uninstall', '/S' -PassThru -Wait
    Start-Sleep 5
    Check ($p.ExitCode -eq 0 -and -not (Test-Path $dir) -and -not (Test-Path $lnk) -and -not (Test-Path $key)) "remove pasta, atalho e entrada em Apps"
    Check ((Get-Content (Join-Path $data 'OpenCleaner.ini') -Raw) -match 'OpenCleanerTestManter') "configurações do usuário continuam em %AppData%\OpenCleaner"
}
finally {
    if (Test-Path "$dir\uninstall.exe") { Start-Process "$dir\uninstall.exe" -ArgumentList '/uninstall', '/S' -Wait; Start-Sleep 4 }
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    if ($hadData) { Copy-Item $dataBackup $data -Recurse }
    Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "`n  configurações do usuário devolvidas: $(if ($hadData) { 'sim' } else { 'não havia nenhuma; nada ficou' })"
}
Write-Host "`n$(if ($fail -eq 0) { 'TUDO OK' } else { "$fail FALHA(S)" }) ($ok verificações)"
exit $fail
