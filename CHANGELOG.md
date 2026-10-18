# Changelog

All notable changes to OpenCleaner. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Custom clean: cache and logs of the **Epic Games Launcher**, **Battle.net** and the **EA app**, and logs and
  crash reports of the **Riot Client**. Cookies, sessions and databases are kept, so nobody is signed out;
  folders that would need administrator rights are left out.
- Gaming: **system clock forced in the boot configuration** (`useplatformclock`, `useplatformtick`,
  `disabledynamictick`, a common booster tweak). Shown only when running as administrator, since reading the
  BCD needs it; applying deletes only those values and asks for a restart.
- Gaming: **plugged-in power mode** recommendation on laptops using the Balanced plan when the power mode on
  AC is not *Best performance* (read-only; opens *Settings > System > Power*).
- Gaming: **PC Game Pass games** installed by the Xbox app (`XboxGames\<game>\Content`) are found and can be
  set to the dedicated graphics card; `gamelaunchhelper.exe` is ignored.

### Changed

- The changelog and the release notes are now in English.

### Security

- Only the three clock values can become `bcdedit` arguments; any other boot setting is never read as a
  problem nor deleted.

## [1.1.0] - 2026-10-17

### Added

- **Gaming** (new sidebar item and a card on the Health check): finds and undoes what "game boosters" leave
  behind in Windows, with a `.reg` backup before applying.
  - Settings left by optimizers: Game Mode turned off, a policy that blocks the Xbox Game Bar, apps barred
    from running in the background, a copy of Ultimate Performance active on a laptop, and missing Xbox apps
    (Xbox Identity Provider, Xbox TCUI, Gaming Services), which only open their Microsoft Store page.
  - Security: Chrome and Edge automatic updates disabled, Windows Update turned off, Defender turned off by
    policy, UAC turned off and Spectre/Meltdown protections turned off.
  - Graphics card for games: on laptops with two graphics cards, finds the games (Steam, Epic Games and the
    ones Windows has seen run) and sets them to *High performance*, the same option as Settings.
  - Recommended for gaming (read-only, opens the Windows page): background recording, GPU scheduling,
    optimizations for windowed games and pointer precision. Performance × security: Memory integrity, as
    information only.

### Changed

- The shader cache (DirectX, NVIDIA, AMD) starts unchecked in Custom clean, with a warning: deleting it makes
  games recompile their shaders and stutter for the first few matches.
- Health check with four cards and shorter texts.

### Security

- Game discovery hardened against malicious manifests and registry entries: a Steam `installdir` is accepted
  only as a folder name (an absolute path turned `C:\Windows` into a game folder), an Epic `LaunchExecutable`
  must stay inside the game folder, executables under the Windows folder are ignored, paths are normalized
  before comparing, a game folder that is a junction is not searched, and the 30-executable limit applies
  within each folder.
- `GoogleUpdater*` services whose names the Service Control Manager would not accept (spaces, `&`, quotes)
  are never offered.

### Fixed

- Duplicated, broken paragraph (`testsun-real.ps1`) in the READMEs.

### Tests

- `tests/run-real.ps1`: registry, service and game-library sandboxes for every Gaming item, including false
  positives, backup, fix and restore; attacks (manifests with absolute paths, `..` and separators, a junction
  to System32, wrong value types, service names with `&`, 60 rounds of random registry data) and performance
  (300 games, 1000 entries in the Windows game list, 2000 background apps, a deep tree of executables).
- `tests/run-smoke.ps1`: screenshots of the Gaming page, with a test game and a selected recommendation, and
  *Apply checked* end to end against a registry sandbox (with and without administrator, backup and cancel).
- `tests/run-admin-tests.ps1`: real startup-type changes of a temporary test service through the Service
  Control Manager (run as administrator).

## [1.0.1] - 2026-10-17

### Security

- Before deleting each file, checks whether any folder between it and the base folder became a junction or
  link after the analysis (stops a cleanup running as administrator from deleting system files).
- Rule paths are made canonical (`..`, `.`, duplicate backslashes, trailing dots, 8.3 names and `\\?\`
  prefixes) before being compared with the protected folders.
- Refuses "delete everything" rules right under the user profile (Downloads, OneDrive, projects), a drive root
  or a protected folder (`%AppData%\*`); Downloads, OneDrive and the app's own backups become protected.
- Secure deletion does not overwrite files with more than one hard link.
- The Duplicate finder skips cloud-only and offline files instead of downloading them.
- The app and the installer only load DLLs from System32 (DLL planting protection).
- winget IDs and registry keys are validated before becoming command-line arguments.
- `SECURITY.md` with the threat model and how to report vulnerabilities.

### Tests

- `tests/run-real.ps1`: every rule run in a sandbox, attacks on the engine, registry, startup, uninstaller,
  disk, duplicates, winget, drivers, `/AUTO`, robustness and performance.
- `tests/run-installer-tests.ps1`: install, update with the app open, planted DLLs and uninstall.

## [1.0.0] - 2026-10-17

First public release.

### Added

- **Health check** with a system drive meter (used, can be freed and free) and a summary of registry,
  startup and updates.
- **Custom clean** with more than 70 built-in rules (Windows, Chromium and Firefox browsers, applications),
  detection of installed programs, a warning about open programs, custom folders, exclusions, an option to
  keep temporary files newer than 24 hours, secure deletion and `winapp2.ini` support.
- **Registry**: seven categories of orphaned entries, with a `.reg` backup before fixing.
- **Update programs** through winget and **Update drivers** through Windows Update, with a restore point
  before installing drivers.
- **Uninstall programs**, **Startup**, **Disk analyzer** and **Duplicate finder**.
- Silent `/AUTO` mode for scheduled cleaning.
- Per-user installer (no administrator) with uninstall from *Settings > Apps*, a portable version and SHA-256
  sums.
- Interface with keyboard navigation, visible focus, AA contrast and DPI scaling.
- App, installer and uninstaller in Portuguese and English, following the Windows language by default, with a
  selector in the installer and in *Options*; saved cleaning selections work in both languages.

[Unreleased]: https://github.com/flavioricardo/OpenCleaner/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.1.0
[1.0.1]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.0.1
[1.0.0]: https://github.com/flavioricardo/OpenCleaner/releases/tag/v1.0.0
