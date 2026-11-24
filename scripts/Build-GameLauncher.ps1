#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory, [switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$selectedRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $selectedRoot 'Rizline.exe') -PathType Leaf)) { throw 'Selected directory does not contain Rizline.exe.' }
$output = Join-Path $repoRoot 'artifacts/game-launcher'
dotnet build (Join-Path $repoRoot 'tools/RizToolbox.GameLauncher/RizToolbox.GameLauncher.csproj') -c Release -o $output --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Game launcher build failed.' }
@{SchemaVersion=1;GameDirectory=$selectedRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'riztoolbox-launch.json') -Encoding utf8
if ($Test) {
    dotnet (Join-Path $output 'RizToolbox.GameLauncher.dll') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Launcher configuration checks failed.' }
}
Write-Output "Local mod launcher: $(Join-Path $output 'RizToolbox.GameLauncher.exe')"
