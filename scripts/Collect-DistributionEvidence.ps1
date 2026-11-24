#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$RunOwnedFixtures
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageDistribution.Common.ps1')
$root = [IO.Path]::TrimEndingDirectorySeparator($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PackageRoot))
Assert-DistributionPathWithoutLinks $ManifestPath
$manifestSha256 = Get-DistributionHash $ManifestPath
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
Assert-RuntimeDistribution $root $manifest 'Validation'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
Assert-DistributionPathWithoutLinks $output
if ($output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep evidence outside the package payload.' }
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$fixtureResults = @()
if ($RunOwnedFixtures) {
    $helper = Join-Path $root 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
    & (Join-Path $root 'validation/fixtures/JudgeContext.Fixture.exe') $helper 2>&1 | Set-Content -LiteralPath (Join-Path $output 'judge-context.txt') -Encoding utf8
    $judgeExit = $LASTEXITCODE
    $kernel = Join-Path $root 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
    & (Join-Path $root 'validation/fixtures/EngineNodeKernel.EntryFixture.exe') --kernel $kernel --output (Join-Path $output 'kernel-entry.json')
    $kernelExit = $LASTEXITCODE
    $fixtureResults = @(@{name='judge-context';exitCode=$judgeExit},@{name='kernel-entry';exitCode=$kernelExit})
}
if ((Get-DistributionHash $ManifestPath) -cne $manifestSha256) { throw 'The package manifest changed during evidence collection.' }
[ordered]@{
    schemaVersion=1;packageKind='Validation';packageManifestSha256=$manifestSha256
    packageHashesVerified=$true;ownedFixturesRequested=[bool]$RunOwnedFixtures;ownedFixtures=$fixtureResults
    gameExecuted=$false;gameScenariosVerified=$false;productionAdmission=$false;acceptance='preview/unverified'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'distribution-evidence.json') -Encoding utf8
if (@($fixtureResults | Where-Object exitCode -ne 0).Count) { throw 'An owned fixture failed; evidence retained.' }
Write-Output "Distribution evidence: $output"
