#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll' }
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
function Hash([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
if ((Hash $dobby) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the operation publication fixture requires the inspected Dobby image.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$before = @($helper, $fixture, $dobby) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$start = [Diagnostics.ProcessStartInfo]::new($fixture)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($helper)
$start.ArgumentList.Add($dobby)
$child = [Diagnostics.Process]::new()
$child.StartInfo = $start
if (-not $child.Start()) { throw 'Could not start the owned Dobby operation fixture.' }
try {
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(60000)) {
        $child.Kill($true)
        $child.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $output 'fixture-stdout.log'), $stdout.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output 'fixture-stderr.log'), $stderr.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        throw 'The owned Dobby operation fixture exceeded its finite timeout.'
    }
    $exitCode = $child.ExitCode
    $outText = $stdout.GetAwaiter().GetResult()
    $errText = $stderr.GetAwaiter().GetResult()
} finally { $child.Dispose() }
[IO.File]::WriteAllText((Join-Path $output 'fixture-stdout.log'), $outText, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'fixture-stderr.log'), $errText, [Text.UTF8Encoding]::new($false))
$lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Dobby operation fixture failed (exit $exitCode). See $output" }
$value = $lines[0] | ConvertFrom-Json -AsHashtable
if ($value.kind -ne 'native-operation-dobby-fixture' -or $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
    $value.originalCalls -ne 92 -or $value.unpublishedOriginalCalls -ne 1 -or $value.totalActualOriginalCalls -ne 93 -or
    $value.entered -ne 92 -or $value.exited -ne 92 -or $value.uniqueSequenceChecks -ne 92 -or
    $value.originalExceptionCases -ne 16 -or $value.observerExceptionCases -ne 32 -or $value.nativeWorkerCalls -ne 16 -or
    $value.nestedCalls -ne 8 -or $value.logicalDetachCases -ne 4 -or $value.legitimateZeroTokens -ne 4 -or
    $value.preparedTargets -ne 5 -or $value.committedTargets -ne 4 -or $value.commitRejections -ne 4 -or
    $value.configureRejectedTargets -ne 1 -or $value.retainedUnpublishedPreparedTargets -ne 1 -or
    ($value.kinds -join ',') -ne '0,1,2,3' -or $value.frameSize -ne 64 -or $value.realDobbyPreparedCommit -ne $true -or
    $value.exactSourceCallerVerified -ne $true -or $value.prepareRejected -ne $true -or $value.pinnedDobbyVerified -ne $true -or
    $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or $value.installed -ne $false -or
    $value.firstInstallSafetyVerified -ne $false -or $value.concurrentFirstPublicationVerified -ne $false -or
    $value.partialPublicationFailureInjected -ne $false -or $value.exceptionFlagsTransparencyVerified -ne $false -or
    $value.nativeAdmissionGateVerified -ne $false) { throw 'The Dobby operation fixture returned an incomplete result.' }
[IO.File]::WriteAllText((Join-Path $output 'fixture-result.json'), $lines[0]+"`n", [Text.UTF8Encoding]::new($false))
$files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
$unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
@{schemaVersion=1;kind='native-operation-dobby-runner';exitCode=$exitCode;files=$files;allMatched=$unchanged;
    gameCodeExecuted=$false;firstInstallSafetyVerified=$false;installed=$false} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'fixture-manifest.json') -Encoding utf8
if (-not $unchanged) { throw 'A Dobby operation fixture input changed during execution.' }
Write-Output "Native Dobby operation fixture evidence: $output"
