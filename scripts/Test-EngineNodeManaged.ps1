#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$DobbyPath,
    [string]$HelperPath
)
$ErrorActionPreference = 'Stop'
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
if ($HelperPath) {
    if (-not [IO.Path]::IsPathFullyQualified($HelperPath)) { throw 'HelperPath must be an absolute DLL path.' }
    $helper = (Resolve-Path -LiteralPath $HelperPath).Path
} else {
    if (-not $HelperDirectory) { throw 'Provide HelperDirectory or an absolute HelperPath.' }
    $helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
}
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($managed) -ne 'RizToolbox.Runtime.Tests.dll') { throw 'Use Runtime.Tests for the actual engine-node bridge/dispatcher/ledger regression.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
if ((Hash $dobby) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') { throw 'Use the inspected Dobby Prepare/Commit DLL.' }
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
        foreach ($argument in @($managed, '--engine-node-managed', $mode)) { $start.ArgumentList.Add($argument) }
        $start.Environment['RIZ_ENGINE_NODE_MANAGED_FIXTURE'] = $fixture
        $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helper
        $start.Environment['RIZ_ENGINE_NODE_DOBBY'] = $dobby
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start self-owned engine-node child $mode." }
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
        if ($timedOut) { throw "Owned engine-node child $mode exceeded its finite timeout." }
        $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Engine-node $mode failed (exit $exitCode): $errText $outText" }
        $value = $lines[0] | ConvertFrom-Json -AsHashtable
        $unknown = $mode -in @('original-seh','original-cpp','observer-fault')
        $expectedCaptured = switch ($mode) { 'normal' {9} 'detach' {7} default {4} }
        $expectedOriginals = switch ($mode) { 'normal' {9} 'detach' {11} default {8} }
        $expectedNodes = switch ($mode) { 'normal' {2} 'detach' {2} 'observer-fault' {1} default {0} }
        if ($value.kind -ne 'native-engine-node-managed-fixture' -or $value.mode -ne $mode -or
            $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or $value.modeVerified -ne $true -or
            $value.configuredChannels -ne 4 -or $value.attachedChannels -ne 4 -or $value.sharedPeerSinks -ne 4 -or
            $value.actualDobbyPreparedTargets -ne 4 -or $value.actualDobbyCommittedTargets -ne 4 -or
            $value.actualOriginalCalls -ne $expectedOriginals -or $value.physicalCaptures -ne $expectedCaptured -or
            $value.completionCount -ne $expectedCaptured -or $value.disposalCount -ne $expectedCaptured -or
            $value.peerCompletionCount -ne $expectedCaptured -or $value.peerDisposalCount -ne $expectedCaptured -or
            $value.nativeToClrCallbackVerified -ne $true -or $value.actualDobbyPublicationVerified -ne $true -or
            $value.productionBridgeAndDispatcherVerified -ne $true -or $value.actualNodeLedgerCaptureVerified -ne $true -or
            $value.oneSharedLedgerAcrossFourKinds -ne $true -or $value.actualFrameSharedByTwoSinks -ne $true -or
            $value.nativeInFlight -ne 0 -or $value.dispatcherInFlight -ne 0 -or $value.ledgerOutstanding -ne 0 -or
            $value.ledgerUnknown -ne $unknown -or $value.ledgerPending -ne $true -or $value.engineCancelled -ne $false -or
            $value.nativeFixtureScalarValuesControlled -ne $true -or $value.metadataIdentityControlled -ne $true -or
            $value.actualIl2CppMetadataResolved -ne $false -or $value.productionObjectBackendExecuted -ne $false -or
            $value.firstInstallReceiptUsed -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
            $value.concurrentFirstPublicationVerified -ne $false -or $value.productionEngineHooksPublished -ne $false -or
            $value.exceptionFlagsTransparencyVerified -ne $false -or $value.nativeExceptionsCaughtBeforePInvoke -ne $true -or
            $value.engineCancellationExecuted -ne $false -or $value.schedulerClosureVerified -ne $false -or
            $value.futureStartClosureVerified -ne $false -or $value.gameCodeExecuted -ne $false -or
            $value.gameRuntimeVerified -ne $false -or $value.installed -ne $false -or $value.diskHashesUnchanged -ne $true -or
            $value.results.Count -ne 4 -or $value.nodeGenerations.Count -ne $expectedNodes) {
            throw "Engine-node $mode produced an incomplete or overstated result."
        }
        $kinds = @($value.results | ForEach-Object { $_.kind } | Sort-Object)
        if (($kinds -join ',') -ne '0,1,2,3') { throw "Engine-node $mode missed an actual typed channel." }
        $frameSequences = [Collections.Generic.HashSet[ulong]]::new()
        $entered = 0L
        $captured = 0L
        foreach ($result in $value.results) {
            $nativeUnknown = $mode -in @('original-seh','original-cpp') -or ($mode -eq 'observer-fault' -and $result.kind -eq 3)
            $expectedSnapshotFaults = if ($mode -eq 'observer-fault' -and $result.kind -eq 3) {1} else {0}
            if ($result.slot -ne 25 -or $result.Epoch -le 0 -or $result.target -eq 0 -or $result.original -eq 0 -or $result.entry -eq 0 -or
                $result.target -eq $result.original -or $result.target -eq $result.entry -or
                $result.Entered -le 0 -or $result.Entered -ne $result.Exited -or $result.InFlight -ne 0 -or
                $result.Unknown -ne [int]$nativeUnknown -or $result.SnapshotFaults -ne $expectedSnapshotFaults -or $result.OutputFaults -ne 0 -or
                $result.bridgeKnown -eq $nativeUnknown -or $result.dispatcherKnown -eq $unknown -or
                $result.captures -le 0 -or $result.captures -ne $result.peerCaptures -or $result.captures -ne $result.completions -or
                $result.captures -ne $result.peerCompletions -or $result.captures -ne $result.disposals -or
                $result.captures -ne $result.peerDisposals -or $result.frames.Count -ne $result.captures -or
                $result.sourceInstructionsUnchangedAfterAdoption -ne $true) {
                throw "Engine-node $mode lost exact channel identity or paired physical duties."
            }
            $entered += $result.Entered
            $captured += $result.captures
            foreach ($frame in $result.frames) {
                if ($frame.kind -ne $result.kind -or $frame.Sequence -le 0 -or -not $frameSequences.Add([ulong]$frame.Sequence) -or
                    $frame.caller -eq 0 -or $frame.completed -ne $true -or $frame.disposed -ne $true -or
                    -not $frame.ContainsKey('rawReturn') -or $frame.rawReturn -lt 0 -or $frame.rawReturn -gt [uint32]::MaxValue -or
                    ($frame.kind -ne 1 -and $frame.rawReturn -gt 255)) { throw "Engine-node $mode rewrote or reused a physical source frame." }
            }
        }
        if ($entered -ne $expectedOriginals -or $captured -ne $expectedCaptured) { throw "Engine-node $mode native/managed totals disagree." }
        $observedUint32 = $mode -in @('normal','detach','observer-fault')
        if ($value.executeUint32TransportVerified -ne $observedUint32 -or
            ($observedUint32 -and @($value.executeHighBitReturns | Where-Object { $_ -gt 255 }).Count -eq 0) -or
            ($mode -eq 'normal' -and @($value.executeHighBitReturns | Where-Object { $_ -eq 2882400001 }).Count -eq 0)) {
            throw "Engine-node $mode did not preserve actual Execute uint32 EAX values."
        }
        $identityCount = if ($mode -in @('normal','detach')) {2} else {1}
        if ($value.actualIdentityLedgerCaptureVerified -ne $true -or $value.trackerIdentityProvidersControlled -ne $true -or
            $value.actualTrackerBackendExecuted -ne $false -or $value.identityChains.Count -ne $identityCount) {
            throw "Engine-node $mode omitted the actual identity-ledger path or overstated tracker backend evidence."
        }
        foreach ($chain in $value.identityChains) {
            if ($chain.Outstanding -ne 0 -or $chain.Pending -ne $true -or $chain.EngineCancelled -ne $false -or $chain.identities.Count -ne 1) {
                throw "Engine-node $mode leaked or overclaimed its new identity-ledger responsibility."
            }
            $identity = $chain.identities[0]
            if ($identity.Receiver -eq 0 -or $identity.NativeOwner -eq 0 -or $identity.Receiver -eq $identity.NativeOwner -or
                $identity.Enumerator -eq 0 -or $identity.trackerGeneration -lt 17 -or $identity.StartDisposed -ne $true) {
                throw "Engine-node $mode conflated its actual owner and controlled tracker identities."
            }
            if ($mode -in @('normal','detach') -and ($chain.Unknown -ne $false -or $chain.nodeCount -ne 1 -or
                $identity.EngineGeneration -le 0 -or $identity.Node -eq 0 -or $identity.HandleUpgraded -ne $true -or
                $identity.NativeStartReturned -ne $true -or $identity.HandleConfirmed -ne $true -or $identity.ObservedHandle -eq 0)) {
                throw "Engine-node $mode missed synchronous identity correlation or later handle upgrade."
            }
        }
        $directOriginals = 0L
        foreach ($invocation in $value.invocations) {
            if ($invocation.OriginalCalls -ne 1 -or $invocation.NativeChecks -le 0 -or $invocation.NativeFailures -ne 0 -or
                $invocation.originalCaller -eq 0 -or $invocation.node -eq 0) { throw "Engine-node $mode has a failed native original." }
            $directOriginals += $invocation.OriginalCalls + $invocation.NestedCalls
        }
        if ($directOriginals -ne $expectedOriginals) { throw "Engine-node $mode did not execute exactly the measured native originals." }
        if ($mode -in @('normal','detach')) {
            foreach ($node in $value.nodeGenerations) {
                if ($node.Generation -le 0 -or $node.StartSequence -le 0 -or $node.Node -eq 0 -or $node.NativeOwner -eq 0 -or $node.Enumerator -eq 0 -or
                    $node.StartCompleted -ne $true -or $node.FinalCleanupObserved -ne $true -or $node.ActiveExecutions -ne 0 -or
                    $node.OpenCalls -ne 0 -or $node.ExecuteCalls -ne 1 -or $node.ReleaseCalls -ne 1 -or $node.LastReleaseByte -ne 0 -or
                    $node.InlineCleanupUnproven -ne $false) { throw "Engine-node $mode lost its actual Start/Execute/Release generation." }
            }
            if ($value.synchronousNodeAssociationBeforeStartReturnsVerified -ne $true -or $value.freedNodeNotReadAfterOriginalVerified -ne $true) {
                throw "Engine-node $mode omitted synchronous Start or freed-node evidence."
            }
        }
        if ($mode -eq 'detach' -and ($value.logicalDetachEmptyCalls -ne 4 -or $value.capturesBeforeLogicalDetach -ne 4 -or
            @($value.results | Where-Object { $_.bindings -ne 2 -or $_.DidDetach -ne $true }).Count -ne 0)) {
            throw 'Engine-node detach/rebind did not retain the original permanent channels.'
        }
        if ($mode -eq 'observer-fault' -and @($value.results | Where-Object { $_.controlledObserverFaults -ne 1 }).Count -ne 0) {
            throw 'Engine-node peer fault did not execute in every actual typed channel.'
        }
        [IO.File]::WriteAllText((Join-Path $output "$mode-result.json"), $lines[0] + "`n", [Text.UTF8Encoding]::new($false))
        $rows.Add($value)
        $processes.Add(@{mode=$mode;exitCode=$exitCode;stdoutSha256=(Hash (Join-Path $output "$mode.stdout.log"));stderrSha256=(Hash (Join-Path $output "$mode.stderr.log"))})
        Write-Output "PASS native engine node actual bridge/dispatcher/ledger: $mode ($($value.checks) checks)."
    }
} finally {
    $files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
    $unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    @{schemaVersion=1;files=$files;allMatched=$unchanged;installed=$false;gameCodeExecuted=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'source-file-check.json') -Encoding utf8
    if (-not $unchanged) { throw 'An engine-node fixture input changed during execution.' }
}
@{schemaVersion=1;kind='native-engine-node-managed-summary';passed=$true;failures=0;independentProcesses=$rows.Count;
    checks=($rows | Measure-Object -Property checks -Sum).Sum;
    actualOriginalCalls=($rows | Measure-Object -Property actualOriginalCalls -Sum).Sum;
    physicalCaptures=($rows | Measure-Object -Property physicalCaptures -Sum).Sum;
    completionCount=($rows | Measure-Object -Property completionCount -Sum).Sum;
    disposalCount=($rows | Measure-Object -Property disposalCount -Sum).Sum;
    actualDobbyPublicationVerified=$true;productionBridgeAndDispatcherVerified=$true;actualNodeLedgerCaptureVerified=$true;
    oneSharedLedgerAcrossFourKinds=$true;actualFrameSharedByTwoSinks=$true;nativeFixtureScalarValuesControlled=$true;
    executeUint32TransportVerified=$true;
    actualIdentityLedgerCaptureVerified=$true;trackerIdentityProvidersControlled=$true;actualTrackerBackendExecuted=$false;
    actualIl2CppMetadataResolved=$false;productionObjectBackendExecuted=$false;firstInstallReceiptUsed=$false;
    firstInstallSafetyVerified=$false;concurrentFirstPublicationVerified=$false;productionEngineHooksPublished=$false;
    engineCancelled=$false;engineCancellationExecuted=$false;schedulerClosureVerified=$false;futureStartClosureVerified=$false;
    gameCodeExecuted=$false;gameRuntimeVerified=$false;installed=$false;modes=@($rows);processes=@($processes);files=$before} |
    ConvertTo-Json -Depth 18 | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
Write-Output "Native engine-node bridge/dispatcher/ledger evidence: $output"
