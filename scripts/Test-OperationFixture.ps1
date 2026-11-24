#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath # Accepted for runner symmetry; this direct-entry fixture never loads Dobby.
)
$ErrorActionPreference = 'Stop'
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Hash([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
$before = @($helper, $fixture) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$start = [Diagnostics.ProcessStartInfo]::new($fixture)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($helper)
$child = [Diagnostics.Process]::new()
$child.StartInfo = $start
if (-not $child.Start()) { throw 'Could not start the owned native operation fixture.' }
try {
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(60000)) {
        $child.Kill($true)
        $child.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $output 'fixture-stdout.log'), $stdout.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output 'fixture-stderr.log'), $stderr.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        throw 'The owned native operation fixture exceeded its finite timeout.'
    }
    $exitCode = $child.ExitCode
    $outText = $stdout.GetAwaiter().GetResult()
    $errText = $stderr.GetAwaiter().GetResult()
} finally { $child.Dispose() }
[IO.File]::WriteAllText((Join-Path $output 'fixture-stdout.log'), $outText, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'fixture-stderr.log'), $errText, [Text.UTF8Encoding]::new($false))
$lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Native operation fixture failed (exit $exitCode). See $output" }
$value = $lines[0] | ConvertFrom-Json -AsHashtable
if ($value.kind -ne 'native-operation-context-fixture' -or $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
    $value.originalCalls -le 0 -or $value.originalCalls -ne $value.entered -or $value.entered -ne $value.exited -or
    $value.uniqueSequenceChecks -ne $value.originalCalls -or $value.originalExceptionCases -ne 12 -or
    $value.observerExceptionCases -ne 24 -or $value.nativeWorkerCalls -ne 12 -or $value.nestedCalls -ne 6 -or
    $value.lateConfigurations -ne 3 -or $value.logicalDetachCases -ne 3 -or $value.legitimateZeroTokens -ne 3 -or
    $value.frameSize -ne 64 -or $value.slotsPerKind -ne 32 -or ($value.kinds -join ',') -ne '0,1,2' -or
    $value.configuredNativeTypedCalls -ne $true -or $value.callbackFaultsContained -ne $true -or
    $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or
    $value.realDobbyPreparedCommit -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
    $value.sequenceExhaustionInjected -ne $false -or $value.lateConfigurationBeforeFirstEntryOnly -ne $true -or
    $value.exceptionFlagsTransparencyVerified -ne $false) { throw 'The native operation fixture returned an incomplete result.' }
$lines[0] | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
$files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
$unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
@{schemaVersion=1;kind='native-operation-context-runner';exitCode=$exitCode;files=$files;allMatched=$unchanged;
    gameCodeExecuted=$false;firstInstallSafetyVerified=$false;installed=$false} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'fixture-manifest.json') -Encoding utf8
if (-not $unchanged) { throw 'A native operation fixture input changed during execution.' }
Write-Output "Native operation fixture evidence: $output"
