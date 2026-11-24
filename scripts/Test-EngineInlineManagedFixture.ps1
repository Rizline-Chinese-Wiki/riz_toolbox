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
    if ($Child.kind -ne 'native-engine-inline-managed-integration' -or $Child.schemaVersion -ne 1 -or
        $Child.mode -ne $Mode -or $Child.passed -ne $true -or $Child.failures -ne 0 -or $Child.checks -le 0 -or
        $Child.nativeChecks -le 0 -or $Child.nativeFailures -ne 0) {
        $issues.Add('The actual managed child did not confirm its exact mode and nonzero successful native/managed checks.')
    }
    foreach ($name in @('actualNativeToClrCallbacksVerified', 'actualConfiguredBridgeAndDispatcherVerified',
        'actualIdentityNodeInlineLedgerVerified', 'sourceModuleIsOwnedDll', 'actualBeginReplayVerified',
        'actualStartCompleteBeforeDisposeVerified', 'finalCleanupAfterActualDisposeVerified', 'trackerIdentityProvidersControlled')) {
        if ($Child.$name -ne $true) { $issues.Add("Missing true integration proof: $name") }
    }
    foreach ($name in @('actualTrackerBackendExecuted', 'engineCancelled', 'engineCancellationCompleted',
        'futureProducerClosureVerified', 'schedulerClosureVerified', 'productionAdmissionVerified',
        'productionPublishedAdoptionUsed', 'gameExecuted', 'installed')) {
        if ($Child.$name -ne $false) { $issues.Add("Missing false integration boundary: $name") }
    }
    $expectedStarts = if ($Mode -in @('nested', 'address-reuse')) { 2 } else { 1 }
    $expectedFree = if ($Mode -eq 'retained') { 0 } else { $expectedStarts }
    $expectedRetained = if ($Mode -eq 'retained') { $expectedStarts } else { 0 }
    if ($Child.configuredChannels -ne 4 -or $Child.attachedChannels -ne 4 -or
        $Child.nativeStarts -ne $expectedStarts -or $Child.synchronousNativeExecutes -ne $expectedStarts -or
        $Child.typedCompletions -ne ($expectedStarts * 2) -or $Child.typedDisposals -ne ($expectedStarts * 2) -or
        $Child.actualSideEvents -ne ($expectedStarts * 4) -or $Child.actualVirtualFreeEndpoints -ne $expectedFree -or
        $Child.actualRetainedCleanupEndpoints -ne $expectedRetained -or $Child.nativeAddressReuse -ne ($Mode -eq 'address-reuse')) {
        $issues.Add('Actual typed/native/side/free/retained counts do not match this mode.')
    }
    # Pending=true is intentional. Observed native return and local drainage do
    # not prove scheduler cancellation or closure of future native producers.
    if ($Child.nativeInFlight -ne 0 -or $Child.ledgerOutstanding -ne 0 -or $Child.ledgerPending -ne $true) {
        $issues.Add('Actual native return/local ledger drainage was not established with cancellation still pending.')
    }
    $nodes = @($Child.nodeGenerations)
    if ($nodes.Count -ne $expectedStarts) { $issues.Add('The full native node generation receipts are missing.') }
    $sequences = [Collections.Generic.HashSet[ulong]]::new()
    $generations = [Collections.Generic.HashSet[long]]::new()
    $endpoint = if ($Mode -eq 'retained') { 3 } else { 2 }
    foreach ($node in $nodes) {
        if ($null -eq $node -or $node.Generation -le 0 -or $node.StartSequence -le 0 -or
            $node.Node -eq 0 -or $null -eq $node.Node -or $node.NativeOwner -eq 0 -or $null -eq $node.NativeOwner -or
            $node.Enumerator -eq 0 -or $null -eq $node.Enumerator -or $node.EnumeratorHandle -le 0 -or
            $node.ActiveExecutions -ne 0 -or $node.OpenCalls -ne 0 -or $node.StartCompleted -ne $true -or
            $node.FinalCleanupObserved -ne $true -or $node.InlineCleanupUnproven -ne $false -or
            $node.InlineBranchObserved -ne $true -or $node.InlineEndpoint -ne $endpoint -or $node.ExecuteCalls -ne 1 -or
            $node.ReleaseCalls -ne 0 -or $node.DetachCalls -ne 0 -or $node.DetachObserved -ne $false -or $node.DetachReturned -ne $false) {
            $issues.Add('A native node generation lacks its real Start completion, exact inline endpoint, or drained execution/call proof.')
        }
        if ($null -ne $node -and $node.StartSequence -gt 0 -and -not $sequences.Add([ulong]$node.StartSequence)) {
            $issues.Add('Native Start sequences were reused across actual node generations.')
        }
        if ($null -ne $node -and $node.Generation -gt 0 -and -not $generations.Add([long]$node.Generation)) {
            $issues.Add('Native node generation identifiers were reused.')
        }
    }
    if ($Mode -eq 'address-reuse' -and $nodes.Count -eq 2 -and
        ($nodes[0].Node -ne $nodes[1].Node -or $nodes[0].Generation -ge $nodes[1].Generation)) {
        $issues.Add('The native address reuse receipt did not create a strictly newer completed generation.')
    }
    $events = @($Child.sideEvents)
    if ($events.Count -ne ($expectedStarts * 4)) { $issues.Add('The full actual side event receipts are missing.') }
    $previousEvent = 0UL
    $parentSequences = [Collections.Generic.HashSet[ulong]]::new()
    foreach ($event in $events) {
        if ($null -eq $event -or $event.StartSequence -le 0 -or -not $sequences.Contains([ulong]$event.StartSequence) -or
            $event.EventSequence -le $previousEvent -or $event.Depth -lt 1 -or $event.Depth -gt 2 -or
            ($event.flags -band 0x08) -eq 0 -or ($event.flags -band 0x10) -ne 0) {
            $issues.Add('A side event lacks its exact actual native Start correlation or known ordered parent receipt.')
        }
        if ($null -ne $event) {
            $previousEvent = [ulong]$event.EventSequence
            if ($event.Depth -eq 2) {
                if ($Mode -ne 'nested' -or $event.ParentStartSequence -le 0 -or
                    -not $sequences.Contains([ulong]$event.ParentStartSequence) -or $event.ParentStartSequence -eq $event.StartSequence) {
                    $issues.Add('A nested actual Start has no distinct matching native Start parent.')
                }
                $null = $parentSequences.Add([ulong]$event.ParentStartSequence)
            } elseif ($event.ParentStartSequence -ne 0) { $issues.Add('An outer actual Start unexpectedly claims a parent.') }
        }
    }
    foreach ($sequence in $sequences) {
        $chain = @($events | Where-Object { $_.StartSequence -eq $sequence })
        $kinds = @($chain | ForEach-Object { $_.Kind }) -join ','
        if ($chain.Count -ne 4 -or $kinds -ne "0,1,$endpoint,4") {
            $issues.Add('An exact actual Start did not emit Entered/Begin/End/Exited once in order.')
            continue
        }
        foreach ($item in $chain[1..3]) {
            if (($item.flags -band 7) -ne 3 -or $item.node -eq 0 -or $item.node -ne $chain[1].node) {
                $issues.Add('Begin, End and Exit did not retain the same valid Begin node snapshot.')
            }
        }
        if ($chain[2].RawStartReturn -ne 0 -or $chain[3].RawStartReturn -ne 0 -or
            ($chain[3].flags -band 0x300) -ne 0x300) {
            $issues.Add('The real End AL and returned Start AL/output-slot read proof disagree.')
        }
    }
    if ($Mode -eq 'nested' -and ($parentSequences.Count -ne 1 -or @($events | Where-Object { $_.Depth -eq 2 }).Count -ne 4)) {
        $issues.Add('The nested mode did not retain exactly one complete child Start chain.')
    }
    $reportedFiles = @($Child.files)
    if ($reportedFiles.Count -ne $NativeInputs.Count) { $issues.Add('The child did not retain both exact native file hash receipts.') }
    foreach ($nativeRow in $NativeInputs) {
        $reportedRows = @($reportedFiles | Where-Object {
            $null -ne $_ -and $_.path -is [string] -and [IO.Path]::IsPathFullyQualified($_.path) -and
            [IO.Path]::GetFullPath($_.path) -eq $nativeRow.path
        })
        if ($reportedRows.Count -ne 1 -or $reportedRows[0].beforeSha256 -ne $nativeRow.sha256 -or $reportedRows[0].afterSha256 -ne $nativeRow.sha256) {
            $issues.Add("The actual child native input hash changed or is missing: $($nativeRow.name)")
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
$manifestPath = Join-Path $binary 'inline-managed-fixture-build.json'
$build = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.kind -ne 'engine-inline-managed-fixture-build' -or
    $build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0) { throw 'The native build manifest does not confirm a successful build.' }
$nativeInputs = @()
foreach ($name in @('RizToolbox.EngineNodeKernel.dll', 'RizEngineInline.ManagedFixture.dll')) {
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
$cases = @('free', 'retained', 'nested', 'address-reuse')
$childEnvironment = [ordered]@{
    RIZ_ENGINE_INLINE_MANAGED_HELPER = Join-Path $binary 'RizToolbox.EngineNodeKernel.dll'
    RIZ_ENGINE_INLINE_MANAGED_FIXTURE = Join-Path $binary 'RizEngineInline.ManagedFixture.dll'
}
$null = New-Item -ItemType Directory -Path $output
$resultPath = Join-Path $output 'fixture-result.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-inline-managed-fixture-cases'; startedUtc = [DateTime]::UtcNow.ToString('o')
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
            executable = $dotnet; arguments = @($assembly, '--engine-inline-managed', $mode)
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
        Write-Output "Inline managed fixture $mode : PID=$($row.pid), exit=$($row.exitCode), passed=$($row.passed)"
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
Write-Output "Engine inline managed fixture cases: $resultPath"
