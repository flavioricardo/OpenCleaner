# Builds the release notes from .github/release-notes.md for a tag.
# Used by release.yml (new releases) and rewrite-release-notes.yml (fixing published ones).
# Windows PowerShell 5.1 reads files without a BOM as ANSI, which broke the accents in the notes of
# 1.0.0-1.1.0 ("sÃ³"); everything here is read and written as UTF-8 without BOM.
param(
    [Parameter(Mandatory)] [string]$Tag,
    [Parameter(Mandatory)] [string]$Repo,
    [Parameter(Mandatory)] [string]$Sums,
    [Parameter(Mandatory)] [string]$Out
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Invalid tag: $Tag" }
if ($Repo -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw "Invalid repository: $Repo" }
$utf8 = New-Object System.Text.UTF8Encoding $false
$template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'release-notes.md'), $utf8).TrimStart([char]0xFEFF)
$sha = [IO.File]::ReadAllText((Resolve-Path $Sums), $utf8).Trim()
$notes = $template.Replace('{TAG}', $Tag).Replace('{REPO}', $Repo).Replace('{SHA256}', $sha)
[IO.File]::WriteAllText($Out, $notes, $utf8)
