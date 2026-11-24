#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30,
    [ValidateSet('publish','occupied','is-done','replace','owner-null','callback-mutate','detach-inflight','detach-nested','start-clear','release-clear','batch','high-eax','callback-seh','cleanup-seh','callback-cpp','cleanup-cpp','observer-seh','observer-reenter','nested-foreign','worker','late-attach','address-reuse')]
    [string[]]$Cases = @('publish','occupied','is-done','replace','owner-null','callback-mutate','detach-inflight','detach-nested','start-clear','release-clear','batch','high-eax','callback-seh','cleanup-seh','callback-cpp','cleanup-cpp','observer-seh','observer-reenter','nested-foreign','worker','late-attach','address-reuse')
)
$ErrorActionPreference = 'Stop'
function AbsolutePath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path is required: $Path" }
    [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 24), [Text.UTF8Encoding]::new($false))
}
$binary = AbsolutePath $BinaryDirectory
$output = AbsolutePath $OutputDirectory
$manifestPath = Join-Path $binary 'wait-fixture-build.json'
$build = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.kind -ne 'engine-wait-fixture-build' -or
    $build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0) { throw 'Use a successfully built self-owned wait fixture.' }
if ($Cases.Count -eq 0 -or @($Cases | Select-Object -Unique).Count -ne $Cases.Count) { throw 'Cases must be nonempty and unique.' }
if (Test-Path -LiteralPath $output) { throw "Use a fresh nonexistent OutputDirectory: $output" }
$fixture = Join-Path $binary 'EngineWait.Fixture.exe'
$kernel = Join-Path $binary 'RizToolbox.EngineNodeKernel.dll'
$inputs = @()
foreach ($name in @('EngineWait.Fixture.exe', 'RizToolbox.EngineNodeKernel.dll')) {
    $path = Join-Path $binary $name
    $buildRows = @($build.files | Where-Object { $_.name -eq $name })
    if ($buildRows.Count -ne 1 -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Hash $path) -ne $buildRows[0].sha256 -or (Get-Item -LiteralPath $path).Length -ne $buildRows[0].bytes) {
        throw "Input differs from the completed build manifest: $name"
    }
    $inputs += [ordered]@{ name = $name; path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
}
$null = New-Item -ItemType Directory -Path $output
$resultPath = Join-Path $output 'fixture-result.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-wait-fixture-cases'; startedUtc = [DateTime]::UtcNow.ToString('o')
    passed = $false; fixtureOnly = $true; gameExecuted = $false; productionAdmissionVerified = $false
    engineCancellationCompleted = $false; productionPatchInstallationVerified = $false; binaryDirectory = $binary; outputDirectory = $output
    timeoutSeconds = $TimeoutSeconds; requestedCases = $Cases; cases = @(); failures = 0; totalChecks = 0L
    inputs = $inputs; buildManifest = [ordered]@{ path = $manifestPath; sha256 = Hash $manifestPath }
    runner = [ordered]@{ path = $PSCommandPath; sha256 = Hash $PSCommandPath }
}
WriteJson $result $resultPath
$locks = [Collections.Generic.List[IO.FileStream]]::new()
try {
    foreach ($inputRow in $inputs) {
        $locks.Add([IO.File]::Open($inputRow.path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        if ((Hash $inputRow.path) -ne $inputRow.sha256) { throw 'Input changed before the read lock was acquired.' }
    }
    foreach ($mode in $Cases) {
        $caseDirectory = Join-Path $output $mode
        $null = New-Item -ItemType Directory -Path $caseDirectory
        $stdoutPath = Join-Path $caseDirectory 'stdout.log'
        $stderrPath = Join-Path $caseDirectory 'stderr.log'
        $rawPath = Join-Path $caseDirectory 'receipt.bin'
        $jsonPath = Join-Path $caseDirectory 'child-result.json'
        $casePath = Join-Path $caseDirectory 'case.json'
        $row = [ordered]@{
            name = $mode; passed = $false; started = $false; timedOut = $false; killed = $false
            pid = $null; exitCode = $null; elapsedMilliseconds = 0; startedUtc = [DateTime]::UtcNow.ToString('o')
            executable = $fixture; arguments = @($kernel, $mode, $rawPath)
            stdout = $stdoutPath; stderr = $stderrPath; raw = $null; childJson = $null; child = $null; issues = @()
        }
        $process = [Diagnostics.Process]::new()
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $stdoutTask = $null; $stderrTask = $null; $stdoutFile = $null; $stderrFile = $null
        try {
            $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $fixture; $start.WorkingDirectory = $binary; $start.UseShellExecute = $false
            $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            foreach ($argument in $row.arguments) { $start.ArgumentList.Add($argument) }
            $process.StartInfo = $start
            if (-not $process.Start()) { throw 'The self-owned wait child did not start.' }
            $row.started = $true; $row.pid = $process.Id
            $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
            $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $row.timedOut = $true; $row.issues += "Timed out after $TimeoutSeconds seconds."
                try { $process.Kill(); $row.killed = $true }
                catch { $row.issues += "Could not terminate this created child: $($_.Exception.Message)" }
                if (-not $process.WaitForExit(10000)) { $row.issues += 'No exit acknowledgement after terminating the created child.' }
            }
            if ($process.HasExited) { $row.exitCode = $process.ExitCode }
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
                        $row.issues += "$($streamRow.name) pipe did not drain; partial bytes were retained."
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
        if ($row.exitCode -ne 0) { $row.issues += "Actual child exit code was $($row.exitCode)." }
        $text = [IO.File]::ReadAllText($stdoutPath, [Text.Encoding]::UTF8)
        $jsonLines = @($text -split '\r?\n' | Where-Object { $_.TrimStart().StartsWith('{') })
        if ($jsonLines.Count -ne 1) { $row.issues += 'Expected one complete actual child JSON result.' }
        else {
            [IO.File]::WriteAllText($jsonPath, $jsonLines[0] + "`n", [Text.UTF8Encoding]::new($false))
            $row.childJson = [ordered]@{ path = $jsonPath; sha256 = Hash $jsonPath }
            try {
                $row.child = $jsonLines[0] | ConvertFrom-Json -Depth 64 -ErrorAction Stop
                $child = $row.child
                if ($child.schemaVersion -ne 1 -or $child.kind -ne 'native-engine-wait-fixture' -or
                    $child.mode -ne $mode -or $child.passed -ne $true -or $child.initialized -ne $true -or
                    $child.failures -ne 0 -or $child.checks -le 0 -or $child.typedEntryInvoked -ne $true -or
                    $child.gameRuntimeVerified -ne $false -or $child.productionAdmissionVerified -ne $false -or
                    $child.engineCancellationCompleted -ne $false) { $row.issues += 'The actual native child result is incomplete or overstated.' }
                if ($child.physicalKindsConfigured -ne 13 -or $child.phaseSitesConfigured -ne 18 -or
                    $child.pageStateIsReleaseEvidence -ne $false -or $child.productionPatchInstallationVerified -ne $false -or
                    $child.allWritersProven -ne $false -or $child.allDispatchersProven -ne $false -or
                    $child.futureSourcesClosed -ne $false -or $child.nodeCalls -le 0 -or $child.nodeFrees -le 0) {
                    $row.issues += 'Actual Wait evidence is missing or overstates production/lifetime coverage.'
                }
                $expectedUnknown = $mode -in @('worker','observer-seh','callback-seh','cleanup-seh','callback-cpp','cleanup-cpp')
                if ($child.unknown -ne $expectedUnknown) { $row.issues += 'Sticky Unknown differs from this actual scenario contract.' }
                if ($mode -in @('occupied','is-done')) {
                    if ($child.observedPhases -ne 0 -or $child.callbacks -ne 0) { $row.issues += 'Fallback falsely produced publication phases.' }
                } elseif ($child.observedPhases -le 0 -or $child.unwindWalks -le 0 -or
                    $child.virtualUnwinds -ne $child.unwindWalks -or $child.statePairs -le 0) {
                    $row.issues += 'No actual phase/unwind/state-pair evidence was produced.'
                }
                if ($mode -ne 'start-clear' -and $child.schedulerCalls -lt 5) {
                    $row.issues += 'The actual scheduler entry smoke calls were not completed.'
                }
                if ($mode -eq 'address-reuse') {
                    if ($child.waitMemReleaseSuccesses -ne 0 -or $child.waitDecommitSuccesses -ne 2 -or
                        $child.ownedReservationRecommits -ne 1 -or $child.reservationReleased -ne $true) {
                        $row.issues += 'Owned reservation decommit/recommit evidence is incomplete.'
                    }
                } elseif ($child.waitDecommitSuccesses -ne 0 -or $child.ownedReservationRecommits -ne 0 -or
                    $child.reservationReleased -ne $false -or $child.waitMemReleaseSuccesses -le 0) {
                    $row.issues += 'Actual MEM_RELEASE evidence is incomplete or confused with reservation reuse.'
                }
                $result.totalChecks += [long]$child.checks
            } catch { $row.issues += "Child JSON validation failed: $($_.Exception.Message)" }
        }
        if (Test-Path -LiteralPath $rawPath -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($rawPath)
            $row.raw = [ordered]@{ path = $rawPath; bytes = $bytes.Length; sha256 = Hash $rawPath }
            if ($bytes.Length -lt 16 -or [BitConverter]::ToUInt32($bytes, 0) -ne 1 -or
                [BitConverter]::ToUInt32($bytes, 4) -ne $bytes.Length -or
                $null -eq $row.child -or $row.child.rawBytes -ne $bytes.Length) { $row.issues += 'Raw receipt header/actual length is missing or invalid.' }
            if ($bytes.Length -ge 16 -and $null -ne $row.child) {
                $rawChecks = [BitConverter]::ToUInt32($bytes, 8)
                $rawFailures = [BitConverter]::ToUInt32($bytes, 12)
                if ($rawChecks -le 0 -or $rawChecks -ne $row.child.rawChecks -or
                    $rawFailures -ne $row.child.rawFailures -or $rawFailures -ne 0 -or
                    $row.child.persistenceChecks -ne 3 -or
                    [long]$row.child.checks -ne [long]$rawChecks + 3 -or
                    $row.child.failures -ne $rawFailures) {
                    $row.issues += 'Raw assertion snapshot differs from actual child totals or persistence checks.'
                }
            }
        } else { $row.issues += 'No actual raw receipt was persisted.' }
        foreach ($inputRow in $inputs) {
            if ((Hash $inputRow.path) -ne $inputRow.sha256) { $row.issues += "Input changed during the case: $($inputRow.name)" }
        }
        $row.stdoutSha256 = Hash $stdoutPath; $row.stderrSha256 = Hash $stderrPath
        $row.completedUtc = [DateTime]::UtcNow.ToString('o')
        $row.passed = $row.started -and -not $row.timedOut -and $row.exitCode -eq 0 -and $row.issues.Count -eq 0
        if (-not $row.passed) { $result.failures++ }
        WriteJson $row $casePath
        $result.cases += [ordered]@{ name = $mode; passed = $row.passed; pid = $row.pid; exitCode = $row.exitCode
            timedOut = $row.timedOut; path = $casePath; sha256 = Hash $casePath; raw = $row.raw; childJson = $row.childJson; issues = $row.issues }
        WriteJson $result $resultPath
        Write-Output "Wait fixture $mode : PID=$($row.pid), exit=$($row.exitCode), passed=$($row.passed)"
    }
    $result.passed = $result.failures -eq 0 -and $result.cases.Count -eq $Cases.Count
} catch { $result.failure = $_.Exception.Message; throw }
finally {
    foreach ($inputLock in $locks) { $inputLock.Dispose() }
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $result $resultPath
}
if (-not $result.passed) { throw "Wait fixture failed; all case evidence was retained: $resultPath" }
Write-Output "Engine wait fixture cases: $resultPath"
