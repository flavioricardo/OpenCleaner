# Security policy

**English** · [Português (Brasil)](#política-de-segurança)

## Reporting a vulnerability

Please report security issues privately through
[GitHub private vulnerability reporting](https://github.com/flavioricardo/OpenCleaner/security/advisories/new)
instead of a public issue. Include the Windows version, the OpenCleaner version and the steps to reproduce.

Only the latest release receives security fixes.

## Threat model and defenses

A system cleaner deletes files and registry entries and can run as administrator, so mistakes or
malicious input can destroy data or be abused to escalate privileges. These are the risks OpenCleaner
defends against. Each defense is exercised by `tests\run-real.ps1` and `tests\run-installer-tests.ps1`.

| Risk | Defense |
|---|---|
| **Folder swapped for a junction between analysis and cleanup** (classic cleaner privilege escalation: a low-privileged process replaces a Temp subfolder with a link to `C:\Windows\System32` while an elevated cleanup runs) | Right before deleting each file, every folder between the file and the rule's base folder is re-checked; if any became a junction or symbolic link, the file is skipped |
| **Malicious or careless rules** (`winapp2.ini`, custom folders) aiming at system or user folders | Paths are made canonical first (`..`, `.`, duplicate backslashes, trailing dots, 8.3 short names, `\\?\` prefixes). Protected folders (Windows, System32, Program Files, the user profile, Documents, Desktop, Downloads, OneDrive, AppData roots and others) are never wiped. "Everything" rules are refused for direct children of the user profile or of a drive root, and for wildcards directly under a protected folder (`%AppData%\*`) |
| **Junctions and symbolic links inside cleaned folders** | Never followed during analysis; deleting a file symlink removes the link only |
| **Secure deletion through a hard link** (overwriting would zero the data of the other name, possibly a system file) | Files with more than one hard link are deleted without being overwritten |
| **Cloud placeholders** (OneDrive) | The duplicate finder skips cloud-only and offline files instead of downloading them to hash |
| **DLL planting** (a DLL dropped next to the installer in Downloads) | `SetDefaultDllDirectories(System32)` at startup and `DefaultDllImportSearchPaths(System32)` for P/Invoke; tested with 18 commonly hijacked DLLs placed next to both executables |
| **Argument injection** into `winget` or `reg.exe` | winget IDs must match `^[A-Za-z0-9][A-Za-z0-9._+-]*$`; registry keys containing quotes are not exported |
| **Accidental data loss** | Analysis never deletes; destructive actions ask for confirmation; passwords, cookies, history and form data start unchecked; registry fixes offer a `.reg` backup; disk analyzer and duplicate finder use the Recycle Bin |
| **Gaming page as a way to write system settings** (it runs as administrator to fix policies and services) | The list of settings is fixed in the code, not in a rules file. Applying can only delete a value, write a known DWORD, change a service's startup type to automatic or manual, and switch to the Balanced power plan by fixed GUID. Recommendations never write; they open only the fixed pages of the catalog (`ms-settings:` pages, Windows Security, `main.cpl`) and Store links must be `ms-windows-store://pdp/?ProductId=` |
| **Service name injection** | Service names go to the Service Control Manager (`ChangeServiceConfig`) only if they match `^[A-Za-z0-9_.-]+$` and the start type is 2 or 3. Wildcard services (`GoogleUpdater*`) are accepted only when their executable is under `Program Files\Google` |
| **Game library entries** (Steam, Epic Games, Windows game list) | Paths are made canonical before use. A Steam `installdir` must be a plain folder name (no absolute path, separators or `..`); an Epic `LaunchExecutable` must stay inside the game folder; executables under the Windows folder, network and relative paths, Microsoft Store packages and missing files are ignored. A game folder that is itself a junction is not searched, and the search stops at 3 levels and 30 executables. The only write is the per-user graphics preference (`HKCU`) |

## Known limitations

- **Per-user installation.** The installer puts the app in `%LocalAppData%\Programs\OpenCleaner`, which the
  user can write to. Malware already running as that user could replace `OpenCleaner.exe` and wait for
  *Restart as administrator*. Only elevate when an action needs it.
- **Unsigned binaries.** Releases are not code-signed yet, so SmartScreen warns on first run. Verify
  downloads against `SHA256SUMS.txt`, or build from source.
- **The junction re-check narrows the race window but cannot close it completely**: it happens immediately
  before each delete, not atomically with it.
- **Third-party rules.** `winapp2.ini` rules are trusted within the limits above.
- **Tamper Protection.** With it on, Windows may refuse to delete Defender policies; the Gaming page says so
  and leaves that item unchecked instead of retrying.

---

## Política de segurança

[English](#security-policy) · **Português (Brasil)**

**Como reportar:** use o
[relato privado de vulnerabilidades do GitHub](https://github.com/flavioricardo/OpenCleaner/security/advisories/new),
não uma issue pública. Informe a versão do Windows, a versão do OpenCleaner e como reproduzir. Só a versão
mais recente recebe correções.

**Defesas:** troca de pasta por junção entre a análise e a limpeza (verificada antes de cada arquivo);
regras maliciosas ou amplas demais (caminhos canônicos, pastas protegidas, recusa de "tudo" logo abaixo do
perfil, da raiz da unidade ou de pastas protegidas); junções e links nunca seguidos; exclusão segura não
sobrescreve links físicos; duplicados não baixam arquivos da nuvem; DLL plantada ao lado do executável
ignorada; IDs do winget e chaves do registro validados antes de virar argumento. Na página Jogos, a lista de
ajustes é fixa no código; aplicar só apaga um valor, grava um DWORD conhecido, muda o tipo de inicialização
de um serviço para automático ou manual e ativa o plano Equilibrado; recomendações nunca gravam e só abrem
páginas fixas do Windows; nomes de serviço passam por `^[A-Za-z0-9_.-]+$` antes do Gerenciador de Serviços e
curingas (`GoogleUpdater*`) só valem com o executável em `Program Files\Google`; nas bibliotecas de jogos,
os caminhos viram canônicos antes do uso, o `installdir` da Steam tem de ser só um nome de pasta, o
`LaunchExecutable` da Epic tem de ficar dentro da pasta do jogo, executáveis da pasta do Windows, caminhos de
rede ou relativos e arquivos inexistentes são ignorados, e pasta de jogo que é junção não é vasculhada. Cada defesa é testada em
`tests\run-real.ps1` e `tests\run-installer-tests.ps1`.

**Limitações conhecidas:** instalação por usuário em pasta gravável pelo usuário; executáveis sem assinatura
digital; a verificação contra junções reduz, mas não elimina, a janela de corrida; regras do `winapp2.ini`
são confiáveis dentro desses limites; com a Proteção contra Adulteração ligada, o Windows pode recusar a
remoção das políticas do Defender, e a página Jogos avisa e deixa esse item desmarcado.
