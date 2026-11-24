#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll' }
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($managed) -ne 'RizToolbox.Runtime.Tests.dll') { throw 'Use the Runtime.Tests assembly for adopted bootstrap validation.' }
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
if ((Hash $dobby) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') { throw 'The actual bootstrap callback fixture requires the inspected Dobby image.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$before = @($managed, $fixture, $helper, $dobby) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$rows = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
try {
    foreach ($mode in @('normal','original-seh','original-cpp','detach','observer-fault')) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($managed, '--operation-bootstrap-managed', $mode)) { $start.ArgumentList.Add($argument) }
        $start.Environment['RIZ_OPERATION_MANAGED_FIXTURE'] = $fixture
        $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helper
        $start.Environment['RIZ_OPERATION_BOOTSTRAP_DOBBY'] = $dobby
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start owned operation bootstrap child $mode." }
        try {
            $stdout = $child.StandardOutput.ReadToEndAsync()
            $stderr = $child.StandardError.ReadToEndAsync()
            $timedOut = -not $child.WaitForExit(60000)
            if ($timedOut) { $child.Kill($true); $child.WaitForExit() }
            $exitCode = $child.ExitCode
            $outText = $stdout.GetAwaiter().GetResult()
            $errText = $stderr.GetAwaiter().GetResult()
        } finally { $child.Dispose() }
        [IO.File]::WriteAllText((Join-Path $output "$mode.stdout.log"), $outText, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output "$mode.stderr.log"), $errText, [Text.UTF8Encoding]::new($false))
        if ($timedOut) { throw "Owned operation bootstrap child $mode exceeded its finite timeout." }
        $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Operation bootstrap CLR $mode failed (exit $exitCode): $errText $outText" }
        $value = $lines[0] | ConvertFrom-Json -AsHashtable
        $originals = if ($mode -eq 'normal') { 12 } elseif ($mode -eq 'detach') { 18 } else { 15 }
        $captures = if ($mode -eq 'detach') { 9 } elseif ($mode -in @('original-seh','original-cpp')) { 12 } else { 6 }
        $completions = if ($mode -eq 'observer-fault') { 3 } else { $captures }
        if ($value.kind -ne 'native-operation-bootstrap-managed-fixture' -or $value.mode -ne $mode -or
            $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or $value.modeVerified -ne $true -or
            $value.actualOriginalCalls -ne $originals -or $value.preAttachOriginalCalls -ne 9 -or
            $value.captures -ne $captures -or $value.completions -ne $completions -or $value.disposals -ne $completions -or
            $value.configuredChannels -ne 3 -or $value.attachedChannels -ne 3 -or $value.sharedPeerSinks -ne 3 -or
            $value.actualDobbyPreparedTargets -ne 3 -or $value.actualDobbyCommittedTargets -ne 3 -or $value.nativeInFlight -ne 0 -or
            $value.results.Count -ne 3 -or $value.nativeToClrCallbackVerified -ne $true -or $value.actualDobbyPublicationVerified -ne $true -or
            $value.productionBridgeAndSharedBrokerVerified -ne $true -or $value.bootstrapUnattachedOriginalsVerified -ne $true -or
            $value.immutableNativeIdentityVerified -ne $true -or $value.sourceInstructionsUnchangedAfterAdoption -ne $true -or
            $value.metadataIdentityControlled -ne $true -or $value.nativeExceptionsCaughtBeforePInvoke -ne $true -or
            $value.actualIl2CppMetadataResolved -ne $false -or $value.actualLedgerCaptureVerified -ne $false -or
            $value.firstInstallReceiptUsed -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
            $value.concurrentFirstPublicationVerified -ne $false -or $value.oldCallAcrossAttachVerified -ne $false -or $value.exceptionFlagsTransparencyVerified -ne $false -or
            $value.engineCancellationExecuted -ne $false -or $value.gameCodeExecuted -ne $false -or
            $value.gameRuntimeVerified -ne $false -or $value.installed -ne $false -or $value.diskHashesUnchanged -ne $true) {
            throw "Operation bootstrap CLR $mode produced an incomplete or overstated result."
        }
        if ((@($value.results | ForEach-Object { $_.kind } | Sort-Object) -join ',') -ne '0,1,2') { throw 'The actual bootstrap test did not execute three typed channels.' }
        foreach ($result in $value.results) {
            if ($result.slot -ne 22 -or $result.Epoch -le 0 -or $result.target -eq 0 -or $result.original -eq 0 -or
                $result.entry -eq 0 -or $result.target -eq $result.original -or $result.Entered -ne $result.Exited -or
                $result.InFlight -ne 0 -or $result.Unknown -ne 0 -or $result.EnterFaults -ne 0 -or $result.ExitFaults -ne 0 -or
                $result.factoryCalls -ne 1 -or $result.brokerEngineCalls -ne 0 -or $result.nativeKnown -ne $true -or
                $result.brokerKnown -ne ($mode -ne 'observer-fault') -or $result.preAttach.Count -ne 3 -or
                $result.sourceInstructionsUnchangedAfterAdoption -ne $true -or
                $result.controlledObserverFaults -ne $(if ($mode -eq 'observer-fault') { 1 } else { 0 })) {
                throw "Operation bootstrap CLR $mode lost immutable identity or final physical responsibility."
            }
            foreach ($pre in $result.preAttach) {
                if ($pre.OriginalCalls -ne 1 -or $pre.Returned -ne $(if ($pre.mode -eq 0) { 1 } else { 0 }) -or
                    $pre.Caught -ne $(if ($pre.mode -eq 0) { 0 } else { 1 })) { throw 'Unattached original forwarding did not retain normal/exception facts.' }
            }
        }
        [IO.File]::WriteAllText((Join-Path $output "$mode-result.json"), $lines[0] + "`n", [Text.UTF8Encoding]::new($false))
        $rows.Add($value)
        $processes.Add(@{mode=$mode;exitCode=$exitCode;stdoutSha256=(Hash (Join-Path $output "$mode.stdout.log"));stderrSha256=(Hash (Join-Path $output "$mode.stderr.log"))})
        Write-Output "PASS native operation bootstrap CLR: $mode ($($value.checks) checks)."
    }
} finally {
    $files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
    $unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    @{schemaVersion=1;files=$files;allMatched=$unchanged;installed=$false;gameCodeExecuted=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'source-file-check.json') -Encoding utf8
    if (-not $unchanged) { throw 'An adopted operation bootstrap fixture input changed during execution.' }
}
@{schemaVersion=1;kind='native-operation-bootstrap-managed-summary';passed=$true;failures=0;independentProcesses=$rows.Count;
    checks=($rows | Measure-Object -Property checks -Sum).Sum;actualOriginalCalls=($rows | Measure-Object -Property actualOriginalCalls -Sum).Sum;
    preAttachOriginalCalls=($rows | Measure-Object -Property preAttachOriginalCalls -Sum).Sum;modes=@($rows);processes=@($processes);
    actualDobbyPublicationVerified=$true;nativeToClrCallbackVerified=$true;productionBridgeAndSharedBrokerVerified=$true;
    metadataIdentityControlled=$true;actualIl2CppMetadataResolved=$false;actualLedgerCaptureVerified=$false;
    firstInstallReceiptUsed=$false;firstInstallSafetyVerified=$false;concurrentFirstPublicationVerified=$false;oldCallAcrossAttachVerified=$false;
    exceptionFlagsTransparencyVerified=$false;engineCancellationExecuted=$false;
    gameCodeExecuted=$false;gameRuntimeVerified=$false;installed=$false;files=$before} |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
Write-Output "Native operation bootstrap CLR evidence: $output"
