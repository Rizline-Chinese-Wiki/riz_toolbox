#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$ManagedDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 60,
    [string]$DotNetPath = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $DotNetPath) { $DotNetPath = Get-RizDotnetPath }

$ErrorActionPreference = 'Stop'
function AbsolutePath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path is required: $Path" }
    [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 32), [Text.UTF8Encoding]::new($false))
}
function InputRow([string]$Path, [string]$Role) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing fixture input: $Path" }
    [ordered]@{ path = $Path; name = [IO.Path]::GetFileName($Path); role = $Role
        bytes = (Get-Item -LiteralPath $Path).Length; sha256 = Hash $Path }
}
function CheckChild([object]$Child, [string]$Mode, [object[]]$NativeInputs) {
    $issues = [Collections.Generic.List[string]]::new()
    if ($Child.kind -ne 'native-engine-scheduler-managed-integration' -or $Child.schemaVersion -ne 1 -or
        $Child.mode -ne $Mode -or $Child.passed -ne $true -or $Child.failures -ne 0 -or $Child.checks -le 0 -or
        $Child.nativeChecks -le 0 -or $Child.nativeFailures -ne 0 -or $Child.ownedFixtureAbi -ne 2) {
        $issues.Add('The actual child did not confirm its exact scheduler mode and successful nonzero checks.')
    }
    foreach ($name in @('actualNativeToClrCallbacksVerified', 'actualSchedulerDispatcherVerified',
        'actualSchedulerRowLedgerVerified', 'actualNodeIdentityLedgerVerified', 'sourceModuleIsOwnedDll',
        'actualCallerProfilesVerified', 'allRowsRetiredAfterRootDisposeVerified', 'trackerIdentityProvidersControlled',
        'actualNodeRetirementGateVerified', 'nodeGenerationMayRetire')) {
        if ($Child.$name -ne $true) { $issues.Add("Missing true actual integration proof: $name") }
    }
    foreach ($name in @('actualTrackerBackendExecuted', 'engineCancelled', 'engineCancellationCompleted',
        'futureProducerClosureVerified', 'schedulerClosureVerified', 'productionAdmissionVerified',
        'productionPublishedAdoptionUsed', 'gameExecuted', 'installed', 'ledgerEngineCancelled', 'ledgerFutureClosed')) {
        if ($Child.$name -ne $false) { $issues.Add("Missing false verification boundary: $name") }
    }
    $expectedRows = switch ($Mode) { 'repeat' {2} 'multirow' {2} 'remove' {2} 'drain' {3} 'address-reuse' {2} default {1} }
    $expectedCallbacks = switch ($Mode) { 'remove' {0} 'drain' {2} 'repeat' {1} default {$expectedRows} }
    $expectedCleanup = switch ($Mode) { 'high-eax' {0} 'address-reuse' {0} 'repeat' {1} default {$expectedRows} }
    $expectedRelease = [Math]::Max(1, $expectedCleanup)
    $expectedGaps = if ($Mode -in @('remove','drain')) {0} else {$expectedCallbacks}
    $expectedFreedEntries = if ($Mode -eq 'address-reuse') {0} else {$expectedRows}
    if ($Child.configuredSchedulerChannels -ne 6 -or $Child.attachedSchedulerChannels -ne 6 -or
        $Child.configuredNodeChannels -ne 4 -or $Child.attachedNodeChannels -ne 4 -or
        $Child.nativeStarts -ne 1 -or $Child.nativeExecutes -ne 1 -or $Child.nativeReleases -ne $expectedRelease -or
        $Child.nativeCallbacks -ne $expectedCallbacks -or $Child.nativeCleanups -ne $expectedCleanup -or
        $Child.nativeRegistrations -ne $expectedRows -or $Child.nativeFreedEntries -ne $expectedFreedEntries -or
        $Child.actualGapChecks -ne $expectedGaps -or $Child.actualDrainRows -ne $(if ($Mode -eq 'drain') {3} else {0}) -or
        $Child.nativeAddressReuse -ne ($Mode -eq 'address-reuse') -or $Child.gapRetirementGateChecks -ne $expectedGaps) {
        $issues.Add('The real node/scheduler/callback/cleanup/gap/Drain counts differ from the exact scenario.')
    }
    if ($Child.nativeInFlight -ne 0 -or $Child.dispatcherInFlight -ne 0 -or $Child.ledgerOutstanding -ne 0 -or
        $Child.ledgerPending -ne $false -or $Child.nativeNodeFreed -ne $true -or $Child.nodeLedgerUnknown -ne $false) {
        $issues.Add('The actual native scopes, scheduler records or same node ledger did not drain.')
    }
    $rows = @($Child.rowGenerations)
    $seen = [Collections.Generic.HashSet[long]]::new()
    if ($rows.Count -ne $expectedRows) { $issues.Add('Full actual scheduler row generation receipts are missing.') }
    foreach ($row in $rows) {
        if ($null -eq $row -or $row.RegistrationGeneration -le 0 -or $row.NodeGeneration -le 0 -or $row.StartSequence -le 0 -or
            $row.Tree -eq 0 -or $null -eq $row.Tree -or $row.Entry -eq 0 -or $null -eq $row.Entry -or
            $row.Payload -eq 0 -or $null -eq $row.Payload -or $row.State -ne 6 -or $row.NativeReferenceAdded -ne $false) {
            $issues.Add('A row lacks its exact actual registered node generation and final retired state.')
        }
        if ($null -ne $row -and $row.RegistrationGeneration -gt 0 -and -not $seen.Add([long]$row.RegistrationGeneration)) {
            $issues.Add('Actual registration generations were reused.')
        }
    }
    if (@($rows | Where-Object {$_.CleanupCompleted -eq $true}).Count -ne $expectedCleanup) {
        $issues.Add('Cleanup completion was invented or omitted for an actual row.')
    }
    if ($Mode -eq 'repeat' -and @($rows | Where-Object {$_.RepeatCopy -eq $true}).Count -ne 1) {
        $issues.Add('The actual repeat copy was not registered before consuming its old generation.')
    }
    if ($Mode -eq 'address-reuse' -and $rows.Count -eq 2 -and
        ($rows[0].Entry -ne $rows[1].Entry -or $rows[0].RegistrationGeneration -ge $rows[1].RegistrationGeneration)) {
        $issues.Add('Entry address reuse did not produce an independent newer registration generation.')
    }
    $reuse = $Mode -eq 'address-reuse'
    if ($Child.nativeEntryDecommits -ne $(if ($reuse) {2} else {0}) -or
        $Child.nativeEntryRecommits -ne $(if ($reuse) {1} else {0}) -or
        $Child.nativeEntryReservationReleased -ne $reuse -or
        ($reuse -and ($Child.nativeEntryGapState -ne 0x2000 -or $Child.nativeEntryGapQuerySucceeded -ne $true)) -or
        ($expectedGaps -eq 0 -and ($Child.nativeEntryGapState -ne 0 -or $Child.nativeEntryGapQuerySucceeded -ne $false)) -or
        $Child.nativeEntryGapPageStateDiagnostic -ne (-not $reuse) -or
        $Child.addressReuseWithinOwnedReservationOnly -ne $reuse -or $Child.systemAddressReleasedAndReacquiredVerified -ne $false -or
        ($reuse -and ($null -eq $Child.entryReservation -or $Child.entryReservation -eq 0)) -or
        (-not $reuse -and $Child.entryReservation -ne 0)) {
        $issues.Add('The exact owned reservation/decommit/recommit evidence is missing or claims system-address reacquisition.')
    }
    if ($Child.oldGenerationSystemReleaseSuccessVerified -ne (-not $reuse) -or
        $Child.oldGenerationReleaseEvidence -ne 'Exact successful native VirtualFree return, per-row StorageRetired and independent release counters; not current page state after CLR callbacks.') {
        $issues.Add('Old-generation release proof must come from actual native release returns and exact row counters, not current address state.')
    }
    $gaps = @($Child.gaps)
    if ($gaps.Count -ne $expectedGaps) { $issues.Add('Actual consume/callback page-state gap receipts are missing.') }
    foreach ($gap in $gaps) {
        if (($reuse -and ($gap.entryPageState -ne 0x2000 -or $gap.entryPageQuerySucceeded -ne $true -or $gap.entryStorageUnmapped -ne $true)) -or
            (-not $reuse -and $null -ne $gap.entryStorageUnmapped) -or $gap.pageStateDiagnostic -ne (-not $reuse) -or
            $gap.ownedReservationHeld -ne $reuse -or
            $gap.callbackNotEntered -ne $true -or $gap.Pending -ne $true -or $gap.Outstanding -le 0) {
            $issues.Add('Actual entry storage/state or retained callback obligation disagrees with the owned gap.')
        }
    }
    $drainDiagnostics = @($Child.drainPageDiagnostics)
    if ($drainDiagnostics.Count -ne $(if ($Mode -eq 'drain') {3} else {0}) -or
        @($drainDiagnostics | Where-Object {$_.pageStateDiagnostic -ne $true}).Count -ne 0) {
        $issues.Add('Drain post-CLR page queries must remain explicit diagnostics, separate from actual release success receipts.')
    }
    if ($Mode -eq 'high-eax' -and @($rows | Where-Object {$_.CallbackRawRax -eq 0xABCDEF0112340001UL -and -not $_.CleanupCompleted}).Count -ne 1) {
        $issues.Add('The complete callback EAX/RAX did not suppress conditional cleanup.')
    }
    $reported = @($Child.files)
    if ($reported.Count -ne $NativeInputs.Count) { $issues.Add('The child did not preserve both exact native file hashes.') }
    foreach ($nativeRow in $NativeInputs) {
        $found = @($reported | Where-Object {$null -ne $_ -and $_.path -is [string] -and
            [IO.Path]::IsPathFullyQualified($_.path) -and [IO.Path]::GetFullPath($_.path) -eq $nativeRow.path})
        if ($found.Count -ne 1 -or $found[0].beforeSha256 -ne $nativeRow.sha256 -or $found[0].afterSha256 -ne $nativeRow.sha256) {
            $issues.Add("The actual native child file changed or has no receipt: $($nativeRow.name)")
        }
    }
    return $issues.ToArray()
}

$binary = AbsolutePath $BinaryDirectory
$managed = AbsolutePath $ManagedDirectory
$output = AbsolutePath $OutputDirectory
$dotnet = AbsolutePath $DotNetPath
foreach ($directory in @($binary, $managed)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing input directory: $directory" }
}
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must be a fresh nonexistent directory: $output" }
$manifestPath = Join-Path $binary 'scheduler-managed-fixture-build.json'
$build = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.kind -ne 'engine-scheduler-managed-fixture-build' -or
    $build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0) { throw 'The native build manifest does not confirm a successful build.' }
$nativeInputs = @()
foreach ($name in @('RizToolbox.EngineNodeKernel.dll', 'RizEngineScheduler.ManagedFixture.dll')) {
    $path = Join-Path $binary $name
    $buildRows = @($build.files | Where-Object { $_.name -eq $name })
    if ($buildRows.Count -ne 1 -or -not [IO.Path]::IsPathFullyQualified($buildRows[0].path) -or
        [IO.Path]::GetFullPath($buildRows[0].path) -ne $path) { throw "An exact native build row is required: $name" }
    $nativeRow = InputRow $path 'native'
    if ($nativeRow.sha256 -ne $buildRows[0].sha256 -or $nativeRow.bytes -ne $buildRows[0].bytes) { throw "Native build SHA/length differs: $name" }
    $nativeInputs += $nativeRow
}
$assembly = Join-Path $managed 'RizToolbox.AudioContent.Tests.dll'
$depsPath = Join-Path $managed 'RizToolbox.AudioContent.Tests.deps.json'
$runtimePath = Join-Path $managed 'RizToolbox.AudioContent.Tests.runtimeconfig.json'
$managedInputs = @()
$managedDlls = @(Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File | Sort-Object Name)
foreach ($name in @('RizToolbox.AudioContent.Tests.dll', 'RizToolbox.Adapter.dll', 'RizToolbox.Core.dll',
    'RizToolbox.Runtime.dll', 'RizToolbox.Sdk.dll')) {
    if (@($managedDlls | Where-Object { $_.Name -eq $name }).Count -ne 1) { throw "Required managed assembly is absent: $name" }
}
foreach ($dll in $managedDlls) { $managedInputs += InputRow $dll.FullName 'managed-assembly' }
$managedInputs += InputRow $depsPath 'managed-dependencies'
$managedInputs += InputRow $runtimePath 'managed-runtime-config'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v8.0' -or -not $deps.targets.Contains($deps.runtimeTarget.name)) {
    throw 'The managed fixture must have an explicit .NET 8 dependency target.'
}
foreach ($library in $deps.targets[$deps.runtimeTarget.name].Values) {
    if ($library.Contains('runtime')) {
        foreach ($asset in $library.runtime.Keys) {
            if ([IO.Path]::GetFileName($asset) -ne $asset -or $asset -notlike '*.dll' -or
                @($managedDlls | Where-Object { $_.Name -eq $asset }).Count -ne 1) { throw "A managed runtime dependency is not pinned in ManagedDirectory: $asset" }
        }
    }
}
$inputs = @($nativeInputs) + @($managedInputs) + @((InputRow $manifestPath 'native-build-manifest'), (InputRow $dotnet 'dotnet-host'))
$cases = @('normal', 'high-eax', 'repeat', 'multirow', 'remove', 'drain', 'address-reuse')
$childEnvironment = [ordered]@{
    RIZ_ENGINE_SCHEDULER_MANAGED_HELPER = Join-Path $binary 'RizToolbox.EngineNodeKernel.dll'
    RIZ_ENGINE_SCHEDULER_MANAGED_FIXTURE = Join-Path $binary 'RizEngineScheduler.ManagedFixture.dll'
}
$null = New-Item -ItemType Directory -Path $output
$resultPath = Join-Path $output 'fixture-result.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-scheduler-managed-fixture-cases'; startedUtc = [DateTime]::UtcNow.ToString('o')
    passed = $false; fixtureOnly = $true; gameExecuted = $false; productionAdmissionVerified = $false
    engineCancellationCompleted = $false; futureProducerClosureVerified = $false; schedulerClosureVerified = $false
    binaryDirectory = $binary; managedDirectory = $managed; outputDirectory = $output; timeoutSeconds = $TimeoutSeconds
    requestedCases = $cases; inputs = $inputs; managedAssemblyCount = $managedDlls.Count
    childEnvironmentOverrides = $childEnvironment; parentEnvironmentModified = $false
    runner = [ordered]@{ path = $PSCommandPath; sha256 = Hash $PSCommandPath }
    totalChecks = 0L; totalNativeChecks = 0L; failures = 0; cases = @()
}
WriteJson $result $resultPath
$locks = [Collections.Generic.List[IO.FileStream]]::new()
try {
    foreach ($inputRow in $inputs) {
        $locks.Add([IO.File]::Open($inputRow.path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        if ((Hash $inputRow.path) -ne $inputRow.sha256) { throw "Input changed before its lock was acquired: $($inputRow.path)" }
    }
    foreach ($mode in $cases) {
        $caseDirectory = Join-Path $output $mode
        $null = New-Item -ItemType Directory -Path $caseDirectory
        $stdoutPath = Join-Path $caseDirectory 'stdout.log'
        $stderrPath = Join-Path $caseDirectory 'stderr.log'
        $childJsonPath = Join-Path $caseDirectory 'child-result.json'
        $casePath = Join-Path $caseDirectory 'case.json'
        $row = [ordered]@{
            name = $mode; passed = $false; started = $false; timedOut = $false; killed = $false
            pid = $null; exitCode = $null; elapsedMilliseconds = 0; startedUtc = [DateTime]::UtcNow.ToString('o')
            executable = $dotnet; arguments = @($assembly, '--engine-scheduler-managed', $mode)
            childEnvironmentOverrides = $childEnvironment; stdout = $stdoutPath; stderr = $stderrPath
            childJson = $null; child = $null; issues = @()
        }
        $process = [Diagnostics.Process]::new()
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $stdoutTask = $null; $stderrTask = $null; $stdoutFile = $null; $stderrFile = $null
        try {
            $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $dotnet; $start.WorkingDirectory = $managed; $start.UseShellExecute = $false
            $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            foreach ($argument in $row.arguments) { $start.ArgumentList.Add($argument) }
            # StartInfo initially inherits the current environment. Modify only
            # this child's two exact fixture paths, never process-level $env:.
            foreach ($key in $childEnvironment.Keys) { $start.Environment[$key] = $childEnvironment[$key] }
            $process.StartInfo = $start
            if (-not $process.Start()) { throw 'The self-owned managed fixture child did not start.' }
            $row.started = $true; $row.pid = $process.Id
            $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
            $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $row.timedOut = $true; $row.issues += "Timed out after $TimeoutSeconds seconds."
                try { $process.Kill(); $row.killed = $true }
                catch { $row.issues += "Could not terminate the exact timed-out child: $($_.Exception.Message)" }
                if (-not $process.WaitForExit(10000)) { $row.issues += 'The created child did not acknowledge exit after termination.' }
            }
            if ($process.HasExited) { $row.exitCode = $process.ExitCode }
            else { $row.issues += 'No child exit code is available.' }
        } catch {
            $row.issues += $_.Exception.Message
            if ($row.started) {
                try {
                    if (-not $process.HasExited) { $process.Kill(); $row.killed = $true; $null = $process.WaitForExit(10000) }
                    if ($process.HasExited) { $row.exitCode = $process.ExitCode }
                } catch { $row.issues += "Created child cleanup failed: $($_.Exception.Message)" }
            }
        } finally {
            $timer.Stop(); $row.elapsedMilliseconds = $timer.ElapsedMilliseconds
            foreach ($streamRow in @(@{ name = 'stdout'; task = $stdoutTask; file = $stdoutFile },
                @{ name = 'stderr'; task = $stderrTask; file = $stderrFile })) {
                try {
                    if ($null -ne $streamRow.task -and -not $streamRow.task.Wait(5000)) {
                        $row.issues += "$($streamRow.name) pipe did not finish; partial output was retained."
                    }
                } catch { $row.issues += "$($streamRow.name) copy failed: $($_.Exception.Message)" }
                finally {
                    if ($null -ne $streamRow.file) {
                        try { $streamRow.file.Flush($true) }
                        catch { $row.issues += "$($streamRow.name) flush failed: $($_.Exception.Message)" }
                        finally { $streamRow.file.Dispose() }
                    }
                }
            }
            $process.Dispose()
        }
        if (-not (Test-Path -LiteralPath $stdoutPath)) { [IO.File]::WriteAllText($stdoutPath, '') }
        if (-not (Test-Path -LiteralPath $stderrPath)) { [IO.File]::WriteAllText($stderrPath, '') }
        $stdoutText = [IO.File]::ReadAllText($stdoutPath, [Text.Encoding]::UTF8)
        if ($row.exitCode -ne 0) { $row.issues += "Child exit code was $($row.exitCode)." }
        $jsonLines = @($stdoutText -split '\r?\n' | Where-Object { $_.TrimStart().StartsWith('{') })
        if ($jsonLines.Count -ne 1) { $row.issues += 'Expected exactly one complete actual child JSON result in stdout.' }
        else {
            # Preserve the actual full JSON line separately; case.json also
            # retains its parsed fields for scanning. There is no raw binary
            # receipt in this integration protocol and none is invented.
            [IO.File]::WriteAllText($childJsonPath, $jsonLines[0] + "`n", [Text.UTF8Encoding]::new($false))
            $row.childJson = [ordered]@{ path = $childJsonPath; sha256 = Hash $childJsonPath; bytes = (Get-Item -LiteralPath $childJsonPath).Length }
            try {
                $row.child = $jsonLines[0] | ConvertFrom-Json -Depth 64 -ErrorAction Stop
                $row.issues += @(CheckChild $row.child $mode $nativeInputs)
                $result.totalChecks += [long]$row.child.checks
                $result.totalNativeChecks += [long]$row.child.nativeChecks
            } catch { $row.issues += "Child JSON parse/contract validation failed: $($_.Exception.Message)" }
        }
        foreach ($inputRow in $inputs) {
            if ((Hash $inputRow.path) -ne $inputRow.sha256) { $row.issues += "Input changed during the child: $($inputRow.name)" }
        }
        $row.stdoutSha256 = Hash $stdoutPath; $row.stderrSha256 = Hash $stderrPath
        $row.completedUtc = [DateTime]::UtcNow.ToString('o')
        $row.passed = $row.started -and -not $row.timedOut -and $row.exitCode -eq 0 -and $row.issues.Count -eq 0
        if (-not $row.passed) { $result.failures++ }
        WriteJson $row $casePath
        $result.cases += [ordered]@{ name = $mode; passed = $row.passed; pid = $row.pid; exitCode = $row.exitCode
            timedOut = $row.timedOut; path = $casePath; sha256 = Hash $casePath; childJson = $row.childJson; issues = $row.issues }
        WriteJson $result $resultPath
        Write-Output "Scheduler managed fixture $mode : PID=$($row.pid), exit=$($row.exitCode), passed=$($row.passed)"
    }
    $result.passed = $result.failures -eq 0 -and $result.cases.Count -eq $cases.Count
} catch {
    $result.failure = $_.Exception.Message
    throw
} finally {
    foreach ($lock in $locks) { $lock.Dispose() }
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $result $resultPath
}
if (-not $result.passed) { throw "One or more self-owned managed fixture cases failed. Evidence was retained: $resultPath" }
Write-Output "Engine scheduler managed fixture cases: $resultPath"
