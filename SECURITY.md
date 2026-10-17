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

## Known limitations

- **Per-user installation.** The installer puts the app in `%LocalAppData%\Programs\OpenCleaner`, which the
  user can write to. Malware already running as that user could replace `OpenCleaner.exe` and wait for
  *Restart as administrator*. Only elevate when an action needs it.
- **Unsigned binaries.** Releases are not code-signed yet, so SmartScreen warns on first run. Verify
  downloads against `SHA256SUMS.txt`, or build from source.
- **The junction re-check narrows the race window but cannot close it completely**: it happens immediately
  before each delete, not atomically with it.
- **Third-party rules.** `winapp2.ini` rules are trusted within the limits above.

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
ignorada; IDs do winget e chaves do registro validados antes de virar argumento. Cada defesa é testada em
`tests\run-real.ps1` e `tests\run-installer-tests.ps1`.

**Limitações conhecidas:** instalação por usuário em pasta gravável pelo usuário; executáveis sem assinatura
digital; a verificação contra junções reduz, mas não elimina, a janela de corrida; regras do `winapp2.ini`
são confiáveis dentro desses limites.
