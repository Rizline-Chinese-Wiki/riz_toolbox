#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($managed) -ne 'RizToolbox.Runtime.Tests.dll') { throw 'Use the Runtime.Tests assembly for actual operation ledger validation.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
$before = @($managed, $fixture, $helper) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$rows = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
try {
    foreach ($mode in @('normal','original-seh','original-cpp','before-fault','after-fault')) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($managed, '--operation-ledger-callbacks', $mode)) { $start.ArgumentList.Add($argument) }
        $start.Environment['RIZ_OPERATION_MANAGED_FIXTURE'] = $fixture
        $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helper
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start owned operation ledger child $mode." }
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
        if ($timedOut) { throw "Owned operation ledger child $mode exceeded its finite timeout." }
        $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Operation ledger $mode failed (exit $exitCode): $errText $outText" }
        $value = $lines[0] | ConvertFrom-Json -AsHashtable
        if ($value.kind -ne 'native-operation-ledger-fixture' -or $value.mode -ne $mode -or $value.passed -ne $true -or
            $value.failures -ne 0 -or $value.checks -le 0 -or $value.modeVerified -ne $true -or
            $value.nativeToClrCallbackVerified -ne $true -or $value.productionBridgeAndBrokerVerified -ne $true -or
            $value.actualLedgerCaptureVerified -ne $true -or $value.actualMutationLedgerVerified -ne $true -or
            $value.actualOriginalCalls -ne 3 -or $value.observedMutations -ne 3 -or
            $value.completionCount -ne 3 -or $value.disposalCount -ne 3 -or $value.results.Count -ne 3 -or
            $value.actualCancellationRequestLedgerVerified -ne $true -or $value.cancellationRequests -ne 2 -or
            $value.cancellationCompletions -ne 2 -or $value.cancellationDisposals -ne 2 -or
            $value.identityProviderControlled -ne $true -or $value.actualTrackerProviderUsed -ne $false -or
            $value.productionStopBindingVerified -ne $false -or
            $value.nativeInFlight -ne 0 -or $value.ledgerNativeInFlight -ne 0 -or $value.controlledRootRecords -le 0 -or
            $value.initialLedgerActivityControlled -ne $true -or $value.metadataGraphRootsControlled -ne $true -or
            $value.actualIl2CppMetadataResolved -ne $false -or $value.productionObjectBackendExecuted -ne $false -or
            $value.firstInstallReceiptUsed -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
            $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or
            $value.exceptionFlagsTransparencyVerified -ne $false -or $value.nativeExceptionsCaughtBeforePInvoke -ne $true -or
            $value.dobbyPublicationVerified -ne $false -or $value.allMutationWritersClosed -ne $false -or
            $value.allFutureListenersClosed -ne $false -or $value.engineCancellationExecuted -ne $false -or
            $value.diskHashesUnchanged -ne $true -or $value.installed -ne $false) {
            throw "Operation ledger $mode produced an incomplete or overstated result."
        }
        $normal = $mode -eq 'normal'
        if ($value.confirmedReturnedMutations -ne $(if ($normal) { 3 } else { 0 })) { throw "Operation ledger $mode confirmed the wrong number of graphs." }
        $kinds = @($value.results | ForEach-Object { $_.kind } | Sort-Object)
        if (($kinds -join ',') -ne '0,1,2') { throw "Operation ledger $mode did not execute all three typed originals." }
        foreach ($result in $value.results) {
            if ($result.OriginalCalls -ne 1 -or $result.nativeFailures -ne 0 -or $result.nativeChecks -le 0 -or
                $result.ledgerObserved -ne 1 -or $result.Completed -ne $true -or
                $result.ConfirmedReturned -ne $normal -or $result.ledgerUnknown -eq $normal -or
                $result.completions -ne 1 -or $result.disposals -ne 1 -or $result.nativeInFlight -ne 0 -or
                $result.ledgerNativeInFlight -ne 0 -or $result.actualActivityReads -le 0 -or $result.initialControlledIdleReads -le 0 -or
                $result.enginePrepare -ne 1 -or $result.engineCommit -ne 1 -or $result.engineInstall -ne 0) {
                throw "Operation ledger $mode has an unpaired or unverified actual native kind."
            }
            if ($result.kind -lt 2 -and ($result.cancellation.ObservedRequests -ne 1 -or $result.cancellation.Outstanding -ne 0 -or
                $result.cancellation.Pending -ne $true -or $result.cancellation.EngineCancelled -ne $false -or
                $result.cancellation.completions -ne 1 -or $result.cancellation.disposals -ne 1 -or
                $result.cancellation.actualSequence -ne $result.Sequence)) { throw "Operation ledger $mode lost its actual cancellation request boundary." }
            if ($result.kind -eq 2 -and $null -ne $result.cancellation) { throw "Operation ledger $mode falsely assigned a Stop operation to the ref2 ABI." }
        }
        [IO.File]::WriteAllText((Join-Path $output "$mode-result.json"), $lines[0] + "`n", [Text.UTF8Encoding]::new($false))
        $rows.Add($value)
        $processes.Add(@{mode=$mode;exitCode=$exitCode;stdoutSha256=(Hash (Join-Path $output "$mode.stdout.log"));stderrSha256=(Hash (Join-Path $output "$mode.stderr.log"))})
        Write-Output "PASS native operation actual mutation ledger: $mode ($($value.checks) checks)."
    }
} finally {
    $files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
    $unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    @{schemaVersion=1;files=$files;allMatched=$unchanged;installed=$false;gameCodeExecuted=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'source-file-check.json') -Encoding utf8
    if (-not $unchanged) { throw 'An operation ledger fixture input changed during execution.' }
}
@{schemaVersion=1;kind='native-operation-ledger-summary';passed=$true;failures=0;independentProcesses=$rows.Count;
    checks=($rows | Measure-Object -Property checks -Sum).Sum;actualOriginalCalls=($rows | Measure-Object -Property actualOriginalCalls -Sum).Sum;
    observedMutations=($rows | Measure-Object -Property observedMutations -Sum).Sum;modes=@($rows);processes=@($processes);
    actualMutationLedgerVerified=$true;initialLedgerActivityControlled=$true;metadataGraphRootsControlled=$true;
    actualCancellationRequestLedgerVerified=$true;identityProviderControlled=$true;actualTrackerProviderUsed=$false;productionStopBindingVerified=$false;
    productionObjectBackendExecuted=$false;actualIl2CppMetadataResolved=$false;firstInstallReceiptUsed=$false;
    gameCodeExecuted=$false;gameRuntimeVerified=$false;firstInstallSafetyVerified=$false;installed=$false;files=$before} |
    ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
Write-Output "Native operation mutation ledger evidence: $output"
