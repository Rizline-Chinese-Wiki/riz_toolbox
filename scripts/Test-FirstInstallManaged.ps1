#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$GameAssemblyPath,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$SourceSnapshotManifest
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
$managedPath = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$gamePath = (Resolve-Path -LiteralPath $GameAssemblyPath).Path
$dobbyFile = (Resolve-Path -LiteralPath $DobbyPath).Path
$helperFile = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$managedDirectory = Split-Path -Parent $managedPath
$testRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin'))
$snapshotProof = $null; $sourceLocks = [Collections.Generic.List[IDisposable]]::new()
if ($SourceSnapshotManifest) {
    $proofPath = (Resolve-Path -LiteralPath $SourceSnapshotManifest).Path
    $proofHash = Hash $proofPath
    $proof = Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
    if ($proof.kind -cne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true -or
        $proof.runnerExitCode -ne 0 -or $proof.child.passed -ne $true -or $proof.child.sourceHashesUnchanged -ne $true -or
        @($proof.snapshotFiles).Count -eq 0) {
        throw 'Only a completed successful source snapshot regression can select an isolated Runtime test output.'
    }
    $snapshotRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $proofPath) 'source-snapshot'))
    $checksRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $proofPath) 'checks'))
    $testRoot = [IO.Path]::GetFullPath((Join-Path $checksRoot 'Runtime/bin'))
    # Runtime.Tests references Adapter, which references Core/Runtime/Sdk.
    # These are the complete declared dependency roles of that frozen graph;
    # arbitrary EXEs, PDBs, XML or other assemblies are not execution inputs.
    $runtimeRoles = @('RizToolbox.Adapter.dll','RizToolbox.Adapter.deps.json','RizToolbox.Core.dll','RizToolbox.Core.deps.json',
        'RizToolbox.Runtime.dll','RizToolbox.Runtime.deps.json','RizToolbox.Runtime.Tests.dll','RizToolbox.Runtime.Tests.deps.json',
        'RizToolbox.Runtime.Tests.runtimeconfig.json','RizToolbox.Sdk.dll','RizToolbox.Sdk.deps.json','profiles/steam-25977441.json')
    $runtimeArtifacts = @()
    foreach ($artifact in $proof.child.artifacts) {
        $artifactPath = [IO.Path]::GetFullPath($artifact.path)
        if (-not $artifactPath.StartsWith($checksRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A child regression artifact is outside its exact checks directory.'
        }
        if ($artifactPath.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = [IO.Path]::GetRelativePath($testRoot, $artifactPath).Replace('\','/')
            if ($relative -cnotin $runtimeRoles) {
                throw 'An isolated Runtime output has an undeclared artifact role.'
            }
            $runtimeArtifacts += $artifact
        }
    }
    foreach ($name in $runtimeRoles) {
        if (@($runtimeArtifacts|Where-Object{[IO.Path]::GetRelativePath($testRoot, $_.path).Replace('\','/') -ceq $name}).Count -ne 1) {
            throw 'A required isolated Runtime output is not uniquely covered by the actual child artifact manifest.'
        }
    }
    try {
        foreach ($path in @($proofPath) + @($proof.snapshotFiles.path) + @($runtimeArtifacts.path)) {
            $ordinary = [IO.Path]::GetFullPath($path)
            for ($cursor = $ordinary; $cursor; $cursor = Split-Path -Parent $cursor) {
                if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    throw 'Snapshot proof paths cannot cross a filesystem link.'
                }
            }
            $sourceLocks.Add([IO.File]::Open($ordinary, 'Open', 'Read', 'Read'))
        }
        if ((Hash $proofPath) -cne $proofHash) { throw 'The source proof changed while it was retained.' }
        foreach ($item in $proof.snapshotFiles) {
            $declared = [IO.Path]::GetFullPath($item.path)
            if (-not $declared.StartsWith($snapshotRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                (Hash $declared) -cne $item.sha256) { throw 'An isolated compiled source differs from its successful frozen snapshot.' }
        }
        foreach ($item in $runtimeArtifacts) {
            if ((Hash $item.path) -cne $item.sha256 -or (Get-Item -LiteralPath $item.path).Length -ne $item.bytes) {
                throw 'An actual child Runtime artifact differs from the completed source proof.'
            }
        }
        $snapshotProof = @{ path = $proofPath; sha256 = $proofHash; snapshotRoot = $snapshotRoot }
    } catch { foreach ($held in $sourceLocks) { $held.Dispose() }; throw }
}
if (($SourceSnapshotManifest -and $managedPath -ine (Join-Path $testRoot 'RizToolbox.Runtime.Tests.dll')) -or
    (-not $SourceSnapshotManifest -and -not $managedDirectory.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) -or
    [IO.Path]::GetFileName($managedPath) -ne 'RizToolbox.Runtime.Tests.dll') {
    foreach ($held in $sourceLocks) { $held.Dispose() }
    throw 'Managed adoption validation must use the repository or exactly proven isolated Runtime.Tests generated output.'
}
$gameHash = '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'
$dobbyHash = '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'
if ((Hash $gamePath) -ne $gameHash -or (Hash $dobbyFile) -ne $dobbyHash) { throw 'Mapped CLR validation requires the exact pinned game and Dobby files.' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$helperDestination = Join-Path $managedDirectory 'RizToolbox.JudgeContext.dll'
Copy-Item -LiteralPath $helperFile -Destination $helperDestination -Force
if ((Hash $helperDestination) -ne (Hash $helperFile)) { throw 'The helper copied beside the actual Runtime assembly differs.' }
$rows = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
try {
    foreach ($mode in @('happy', 'reentry')) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($managedPath, '--first-install-mapped-adoption', $mode, $gamePath, $dobbyFile)) { $start.ArgumentList.Add($argument) }
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start the owned managed adoption $mode child." }
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(180000)) {
            # This is the specific test process created above, never a game.
            $child.Kill($true)
            $child.WaitForExit()
            throw "The owned managed adoption $mode child exceeded its finite timeout."
        }
        $exitCode = $child.ExitCode
        $lines = @(($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()) -split '\r?\n' | Where-Object { $_.Length -gt 0 })
        $child.Dispose()
        $lines | Set-Content -LiteralPath (Join-Path $outputPath "$mode.log") -Encoding utf8
        $jsonLines = @($lines | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $jsonLines.Count -ne 1) { throw "Managed adoption $mode failed (exit $exitCode): $($lines -join [Environment]::NewLine)" }
        $value = $jsonLines[0] | ConvertFrom-Json -AsHashtable
        if ($value.kind -ne 'mapped-image-managed-preinit-adoption' -or $value.mode -ne $mode -or
            -not $value.passed -or $value.failures -ne 0 -or $value.checks -le 0 -or
            $value.gameRuntimeVerified -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
            $value.metadataIdentityControlled -ne $true -or $value.writesStarted -ne 64 -or
            $value.productionListenerReceiptV7Verified -ne $true -or $value.listenerReadonlyAdoptionVerified -ne $true -or
            $value.listenerPublicationVersion -ne 7 -or $value.listenerCapabilityVersion -ne 1 -or
            $value.listenerSlot -ne 31 -or $value.listenerEpoch -le 0 -or $value.listenerRegions -ne 6 -or
            $value.listenerRegisteredFunctions -ne 3 -or $value.listenerPrewriteLookupChecks -ne 12 -or
            $value.listenerPrewriteBoundaryChecks -ne 12 -or $value.listenerObserverAttached -ne $false -or
            $value.listenerCallbacksVerified -ne $false -or $value.listenerNativeInvocations -ne 0 -or
            $value.nativeToClrCallbackVerified -ne $false -or $value.transportInvocations -ne 0 -or
            $value.operationAdoption.ProductionV7Verified -ne $true -or $value.operationAdoption.DeclaredChannels -ne 26 -or
            $value.operationAdoption.SingleEventPublicationVerified -ne $true -or
            $value.operationAdoption.SingleEventMemoryVerified -ne $true -or
            $value.operationAdoption.SingleEventReadonlyAdoption -ne $true -or
            $value.operationAdoption.ReadOnlyChannels -ne 26 -or $value.operationAdoption.UnchangedInstructions -ne $true -or
            $value.operationAdoption.ZeroEngineInstallCalls -ne $true -or $value.operationAdoption.MetadataControlled -ne $true -or
            $value.operationAdoption.ActualGameCallbacksVerified -ne $false -or
            $value.operationAdoption.AttachedChannels -ne $(if ($mode -eq 'happy') { 26 } else { 0 }) -or
            ($mode -eq 'happy' -and ($value.operationAdoption.SharedPeerSinks -ne 26 -or $value.operationAdoption.LogicalDetachChecks -ne 26 -or
                $value.operationAdoption.RejectedWrongTargets -ne 26 -or $value.operationAdoption.RejectedWrongKinds -ne 26 -or
                $value.operationAdoption.RejectedConflictingMetadata -ne 26))) {
            throw "Managed adoption $mode produced an invalid result."
        }
        $single = $value.singleEventPublication
        if ($single.capabilityVersion -ne 1 -or $single.byteSize -ne 416 -or $single.phase -ne 3 -or $single.unknown -ne 0 -or
            $single.operationId -ne 25 -or $single.kind -ne 3 -or $single.slot -ne 31 -or $single.expectedWrites -ne 7 -or
            $single.epoch -le 0 -or $single.parentEpoch -le 0 -or $single.parentEpoch -ne $single.parentRowEpoch -or
            $single.configuredMask -ne 127 -or $single.publishedMask -ne 127 -or
            $single.attemptedWrites -ne 7 -or $single.completedWrites -ne 7 -or $single.registeredFunctionCount -ne 3 -or
            $single.lookupChecks -lt 15 -or $single.boundaryChecks -lt 15 -or $single.blockBytesVerified -ne 4096 -or
            $single.sourceBytesVerified -ne 53 -or $single.nativeLaneConfigured -ne 1 -or $single.nativeObserverAttached -ne 0 -or
            $single.nativeParentsEntered -ne 0 -or $single.nativeChildrenEntered -ne 0 -or $single.actualGameCallbacksVerified -ne $false -or
            (@($single.spanRvas) -join ',') -cne '12324999,12325127,12325133,12325213,12325218,12325403,12325409' -or
            (@($single.spanWidths) -join ',') -cne '9,6,9,5,9,6,9' -or
            (@($single.stubSizes) -join ',') -cne '21,21,21' -or (@($single.unwindSizes) -join ',') -cne '36,36,36') {
            throw "Managed adoption $mode did not preserve the actual complete Single event publication."
        }
        if ($single.observationPhase -cne 'before-managed-observer-adoption') {
            throw 'Single publication facts must identify the initially unattached observation point.'
        }
        $singleBridge = $value.singleEventBridgeAdoption
        if (($mode -eq 'happy' -and ($singleBridge.parentCapacity -ne 128 -or $singleBridge.epoch -ne $single.epoch -or
                $singleBridge.parentEpoch -ne $single.parentEpoch -or $singleBridge.attached -ne 1 -or
                $singleBridge.sharedOwner -ne $true -or $singleBridge.activityKnown -ne $true -or $singleBridge.inFlight -ne 0 -or
                $singleBridge.survivedClrCollection -ne $true -or $singleBridge.nativeParentsEntered -ne 0 -or
                $singleBridge.nativeChildrenEntered -ne 0 -or $singleBridge.actualGameCallbacksVerified -ne $false)) -or
            ($mode -eq 'reentry' -and $null -ne $singleBridge)) {
            throw "Managed adoption $mode did not preserve the actual Single CLR bridge observer and capacity."
        }
        $jsonLines[0] | Set-Content -LiteralPath (Join-Path $outputPath "$mode-result.json") -Encoding utf8
        $rows.Add($value)
        $results.Add(@{ mode=$mode;exitCode=$exitCode;logSha256=(Hash (Join-Path $outputPath "$mode.log")) })
    }
} finally {
    foreach ($held in $sourceLocks) { $held.Dispose() }
    $sourceChecks = @(
        @{path=$gamePath;expected=$gameHash;actual=(Hash $gamePath)},
        @{path=$dobbyFile;expected=$dobbyHash;actual=(Hash $dobbyFile)}
    )
    $unchanged = @($sourceChecks | Where-Object { $_.actual -ne $_.expected }).Count -eq 0
    @{schemaVersion=1;sourceFiles=$sourceChecks;allMatched=$unchanged;gameCodeExecuted=$false;installed=$false} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'source-file-check.json') -Encoding utf8
    if (-not $unchanged) { throw 'Mapped CLR validation source files changed.' }
}
$summary = @{schemaVersion=1;kind='mapped-image-managed-preinit-adoption-summary';passed=$true;
    sourceSnapshot=$snapshotProof;publicationVersion=7;operationCount=26;expectedWrites=64;
    independentProcesses=$rows.Count;checks=($rows | Measure-Object -Property checks -Sum).Sum;failures=0;
    modes=@($rows);processes=@($results);managedAssemblySha256=(Hash $managedPath);helperSha256=(Hash $helperFile);
    gameRuntimeVerified=$false;firstInstallSafetyVerified=$false;installed=$false}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $outputPath 'fixture-result.json') -Encoding utf8
Write-Output "PASS actual CLR pre-init adoption in $($rows.Count) isolated mapped-image processes; no game code executed."
Write-Output "Managed pre-init adoption evidence: $outputPath"
