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
    if ($Child.kind -ne 'native-engine-wait-managed-integration' -or $Child.schemaVersion -ne 1 -or
        $Child.mode -ne $Mode -or $Child.passed -ne $true -or $Child.failures -ne 0 -or $Child.checks -le 0 -or
        $Child.nativeChecks -le 0 -or $Child.nativeFailures -ne 0 -or $Child.ownedFixtureAbi -ne 1) {
        $issues.Add('The actual child did not confirm its exact Wait mode and successful nonzero checks.')
    }
    foreach ($name in @('actualNativeToClrCallbacksVerified','actualWaitDispatcherVerified','actualWaitLedgerVerified',
        'schedulerMaskRouteConfigured','actualNodeIdentityLedgerVerified','sourceModuleIsOwnedDll',
        'actualTypedCompleteDisposeVerified','actualPerGenerationRetirementVerified','trackerIdentityProvidersControlled',
        'actualNativeWaitClrIntegration','nativeReturned','drained')) {
        if ($Child.$name -ne $true) { $issues.Add("Missing true actual integration proof: $name") }
    }
    foreach ($name in @('actualTrackerBackendExecuted','engineCancelled','engineCancellationCompleted',
        'futureProducerClosureVerified','schedulerClosureVerified','productionAdmissionVerified',
        'productionPublishedAdoptionUsed','gameExecuted','installed','ledgerEngineCancelled','ledgerFutureClosed',
        'ledgerPending','ledgerUnknown')) {
        if ($Child.$name -ne $false) { $issues.Add("Missing false verification boundary: $name") }
    }
    if ($Child.actualSchedulerMaskRouteVerified -ne ($Child.nativeCallbacks -gt 0)) {
        $issues.Add('Actual scheduler mask route verification must match whether a real callback occurred in this exact child.')
    }
    $expectedPublications = if ($Mode -in @('replace','republish')) {2} else {1}
    $expectedNodes = if ($Mode -eq 'republish') {2} else {1}
    $expectedLateNullDispatch = if ($Mode -eq 'replace') {1} else {0}
    if ($Child.lateNullDispatchCaptures -ne $expectedLateNullDispatch -or
        $Child.lateNullDispatchCompletes -ne $expectedLateNullDispatch -or
        $Child.lateNullDispatchDisposes -ne $expectedLateNullDispatch -or
        $Child.actualLateNullDispatchVerified -ne ($Mode -eq 'replace')) {
        $issues.Add('The late callback-null Dispatch must retain its full actual scope without reacquiring a retired node.')
    }
    if ($Child.configuredNodeChannels -ne 4 -or $Child.attachedNodeChannels -ne 4 -or
        $Child.configuredSchedulerChannels -ne 6 -or $Child.attachedSchedulerChannels -ne 6 -or
        $Child.configuredWaitFamilies -ne 1 -or $Child.attachedWaitFamilies -ne 1 -or
        $Child.nativeStarts -ne $expectedNodes -or $Child.nativeNodesFreed -ne $expectedNodes -or
        $Child.nativePublications -ne $expectedPublications -or $Child.nativeWaitsFreed -ne $(if($Mode -eq 'replace'){2}else{1}) -or
        $Child.nativeLiveNodes -ne 0 -or $Child.nativeLiveWaits -ne 0 -or
        $Child.nativeInFlight -ne 0 -or $Child.ledgerOutstanding -ne 0 -or
        $Child.nativeCallbacks -ne $Child.capturedCallbacks -or
        $Child.cleanupCaptures -ne $Child.cleanupCompletes -or $Child.cleanupCaptures -ne $Child.cleanupDisposes -or
        $Child.cleanupCaptures -ne $Child.typedCleanupStillPending -or $Child.actualProbeCount -le 0) {
        $issues.Add('Actual Node/M/Wait ownership, free, callback, or Complete/Dispose counters disagree.')
    }
    $publications = @($Child.publications)
    if ($publications.Count -ne $expectedPublications) { $issues.Add('Missing exact actual publication generations.') }
    $seen = [Collections.Generic.HashSet[long]]::new()
    foreach ($record in $publications) {
        if ($null -eq $record -or $record.PublicationGeneration -le 0 -or $record.NodeGeneration -le 0 -or
            $record.Wait -eq 0 -or $record.Node -eq 0 -or $record.StartSequence -le 0 -or
            $record.State -ne 6 -or $record.Committed -ne $true -or $record.ReferenceReturned -ne $true -or
            $record.NodeWaitCleared -ne $true -or $record.OpenDispatches -ne 0 -or $record.OpenCallbacks -ne 0 -or $record.OpenCleanups -ne 0) {
            $issues.Add('A Wait generation was not retired through its exact actual node/reference/dispatch duties.')
        }
        if ($null -ne $record -and -not $seen.Add([long]$record.PublicationGeneration)) { $issues.Add('Publication generation was reused.') }
    }
    $nodes = @($Child.nodeGenerations)
    if ($nodes.Count -ne $expectedNodes -or @($nodes | Where-Object {$_.ActiveExecutions -ne 0 -or $_.OpenCalls -ne 0 -or $_.StartCompleted -ne $true}).Count -ne 0) {
        $issues.Add('Actual node physical contexts did not drain.')
    }
    if ($Mode -eq 'start-clear') {
        if ($nodes.Count -ne 1 -or $nodes[0].InlineCleanupUnproven -ne $true -or $nodes[0].FinalCleanupObserved -ne $false -or
            @($Child.phases | Where-Object {$_.kind -in @(9,10,11)}).Count -ne 3 -or $Child.nativeCallbacks -ne 0) {
            $issues.Add('Owned Start-clear must prove its Wait publication while retaining independent inline Node uncertainty.')
        }
    } elseif (@($nodes | Where-Object {$_.FinalCleanupObserved -ne $true -or $_.InlineCleanupUnproven -ne $false}).Count -ne 0) {
        $issues.Add('A native Node final Release was not observed.')
    }
    if ($Mode -eq 'republish' -and ($publications.Count -ne 2 -or
        $publications[0].NodeGeneration -eq $publications[1].NodeGeneration -or $publications[0].Wait -ne $publications[1].Wait)) {
        $issues.Add('Republish must be two actual node generations at one retained Wait object.')
    }
    if ($Mode -eq 'replace' -and ($publications.Count -ne 2 -or
        $publications[0].NodeGeneration -ne $publications[1].NodeGeneration -or $publications[0].Wait -eq $publications[1].Wait -or $Child.nativeOldReturns -le 0)) {
        $issues.Add('Replace must return the old wait reference while retaining two independent publications for one actual node.')
    }
    if ($Mode -eq 'high-eax' -and ($Child.callbackRaw -le [uint32]::MaxValue -or
        ([uint64]$Child.callbackRaw -band 0xffffffffUL) -eq 1 -or ([uint64]$Child.callbackRaw -band 0xffUL) -ne 1 -or $Child.cleanupCaptures -le 0)) {
        $issues.Add('Wait cleanup must not be normalized to callback EAX/AL semantics.')
    }
    if ($Mode -ne 'start-clear' -and $Child.retirementGateChecks -le 0) { $issues.Add('No live actual publication tested the private per-generation retirement gate.') }
    foreach ($nativeRow in $NativeInputs) {
        $found = @($Child.files | Where-Object {$null -ne $_ -and $_.path -is [string] -and
            [IO.Path]::IsPathFullyQualified($_.path) -and [IO.Path]::GetFullPath($_.path) -eq $nativeRow.path})
        if ($found.Count -ne 1 -or $found[0].beforeSha256 -ne $nativeRow.sha256 -or $found[0].afterSha256 -ne $nativeRow.sha256) {
            $issues.Add("The child's exact native file changed or lost its receipt: $($nativeRow.name)")
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
$manifestPath = Join-Path $binary 'wait-managed-fixture-build.json'
$build = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.kind -ne 'engine-wait-managed-fixture-build' -or
    $build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0) { throw 'The native build manifest does not confirm a successful build.' }
$nativeInputs = @()
foreach ($name in @('RizToolbox.EngineNodeKernel.dll', 'RizToolbox.EngineWaitManagedFixture.dll')) {
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
$cases = @('normal','owner-null','high-eax','start-clear','batch-null','callback-clear','replace','republish')
$childEnvironment = [ordered]@{
    RIZ_ENGINE_WAIT_MANAGED_HELPER = Join-Path $binary 'RizToolbox.EngineNodeKernel.dll'
    RIZ_ENGINE_WAIT_MANAGED_FIXTURE = Join-Path $binary 'RizToolbox.EngineWaitManagedFixture.dll'
}
$null = New-Item -ItemType Directory -Path $output
$resultPath = Join-Path $output 'fixture-result.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-wait-managed-fixture-cases'; startedUtc = [DateTime]::UtcNow.ToString('o')
    passed = $false; actualNativeWaitClrIntegration = $false; fixtureOnly = $true; gameExecuted = $false; productionAdmissionVerified = $false
    engineCancellationCompleted = $false; futureProducerClosureVerified = $false; schedulerClosureVerified = $false
    binaryDirectory = $binary; managedDirectory = $managed; outputDirectory = $output; timeoutSeconds = $TimeoutSeconds
    buildManifest = [ordered]@{ path = $manifestPath; sha256 = Hash $manifestPath }
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
            executable = $dotnet; arguments = @($assembly, '--engine-wait-managed', $mode)
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
        Write-Output "Wait managed fixture $mode : PID=$($row.pid), exit=$($row.exitCode), passed=$($row.passed)"
    }
    $result.passed = $result.failures -eq 0 -and $result.cases.Count -eq $cases.Count
    $result.actualNativeWaitClrIntegration = $result.passed
} catch {
    $result.failure = $_.Exception.Message
    throw
} finally {
    foreach ($lock in $locks) { $lock.Dispose() }
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $result $resultPath
}
if (-not $result.passed) { throw "One or more self-owned managed fixture cases failed. Evidence was retained: $resultPath" }
Write-Output "Engine Wait managed fixture cases: $resultPath"
