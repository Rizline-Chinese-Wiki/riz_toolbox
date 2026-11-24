#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
if (Get-Process -Name Rizline -ErrorAction SilentlyContinue) { throw 'Close Rizline before removal.' }
$installationLock = Enter-InstallLock $gameRoot
try {
$manifestPath = Get-InstallTarget $gameRoot 'BepInEx\RizToolbox\installation.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { Write-Output 'No RizToolbox installation manifest found; nothing removed.'; return }
$manifest = Read-InstallManifest $gameRoot $manifestPath
$manifest = Restore-InstallTransaction $gameRoot $manifestPath $manifest
# Validate every entry before the first removal; malformed manifests cannot partially uninstall.
$retained = [Collections.Generic.List[object]]::new()
foreach ($entry in $manifest.files) {
    $target = Get-InstallTarget $gameRoot $entry.path
    if (Test-Path -LiteralPath $target) {
        if ((Get-InstallHash $target) -eq $entry.sha256) { Remove-Item -LiteralPath $target }
        else { $retained.Add($entry); Write-Warning "Preserved modified file: $($entry.path)" }
    }
}
$manifest.files = $retained.ToArray()
# Preserve the small ownership marker so reinstalling alongside generated logs/cache
# remains distinguishable from adopting somebody else's loader directory.
Write-InstallManifest $manifestPath $manifest
Write-Output 'Removed unchanged owned files. Generated data, modified files and the reinstall ownership marker were preserved.'
} finally { $installationLock.Dispose() }
