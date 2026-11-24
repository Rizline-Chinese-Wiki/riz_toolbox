#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30,
    [ValidateSet('free','retained','nested','address-reuse','seh','cpp','observer-seh','observer-cpp',
        'missing-begin','missing-end','duplicate-begin','duplicate-end','worker','late-attach','wrongsite')]
    [string[]]$Cases = @('free','retained','nested','address-reuse','seh','cpp','observer-seh','observer-cpp',
        'missing-begin','missing-end','duplicate-begin','duplicate-end','worker','late-attach','wrongsite')
)
$ErrorActionPreference = 'Stop'
function AbsolutePath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path is required: $Path" }
    return [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
}
$binary = AbsolutePath $BinaryDirectory
$output = AbsolutePath $OutputDirectory
$manifestPath = Join-Path $binary 'inline-fixture-build.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Missing build manifest: $manifestPath" }
$build = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.schemaVersion -ne 1 -or $build.kind -ne 'engine-inline-fixture-build' -or -not $build.buildSucceeded) {
    throw 'The build manifest does not confirm a successful fixture build.'
}
if ($Cases.Count -eq 0 -or @($Cases | Select-Object -Unique).Count -ne $Cases.Count) { throw 'Cases must be nonempty and unique.' }
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must be a fresh, nonexistent directory: $output" }
$fixture = Join-Path $binary 'EngineInline.Fixture.exe'
$kernel = Join-Path $binary 'RizToolbox.EngineNodeKernel.dll'
$inputRows = @()
foreach ($name in @('EngineInline.Fixture.exe', 'RizToolbox.EngineNodeKernel.dll')) {
    $record = @($build.files | Where-Object { $_.name -eq $name })
    if ($record.Count -ne 1) { throw "Build manifest must contain one exact row for $name" }
    $path = Join-Path $binary $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Hash $path) -ne $record[0].sha256) {
        throw "Fixture input hash does not match the build manifest: $path"
    }
    $inputRows += [ordered]@{ name = $name; path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
}
$null = New-Item -ItemType Directory -Path $output
$resultPath = Join-Path $output 'fixture-result.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-inline-fixture-cases'; startedUtc = [DateTime]::UtcNow.ToString('o')
    passed = $false; fixtureOnly = $true; gameExecuted = $false; productionAdmissionVerified = $false
    engineCancellationCompleted = $false; binaryDirectory = $binary; outputDirectory = $output
    timeoutSeconds = $TimeoutSeconds; requestedCases = $Cases; cases = @(); failures = 0
    inputs = $inputRows; buildManifest = [ordered]@{ path = $manifestPath; sha256 = Hash $manifestPath }
    runner = [ordered]@{ path = $PSCommandPath; sha256 = Hash $PSCommandPath }; totalChecks = 0
}
WriteJson $result $resultPath
$locks = [Collections.Generic.List[IO.FileStream]]::new()
try {
    # Test only the two exact local build artifacts. Each case is its own child
    # process; no process discovery, external attachment, or game access occurs.
    foreach ($path in @($fixture, $kernel)) {
        $locks.Add([IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    }
    foreach ($caseName in $Cases) {
        $caseDirectory = Join-Path $output $caseName
        $null = New-Item -ItemType Directory -Path $caseDirectory
        $stdoutPath = Join-Path $caseDirectory 'stdout.log'
        $stderrPath = Join-Path $caseDirectory 'stderr.log'
        $rawPath = Join-Path $caseDirectory 'receipt.bin'
        $casePath = Join-Path $caseDirectory 'case.json'
        $row = [ordered]@{
            name = $caseName; passed = $false; started = $false; timedOut = $false; killed = $false
            pid = $null; exitCode = $null; elapsedMilliseconds = 0; startedUtc = [DateTime]::UtcNow.ToString('o')
            executable = $fixture; arguments = @($kernel, $caseName, $rawPath)
            stdout = $stdoutPath; stderr = $stderrPath; raw = $null; child = $null; issues = @()
        }
        $process = [Diagnostics.Process]::new()
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $stdoutTask = $null; $stderrTask = $null
        $stdoutFile = $null; $stderrFile = $null
        try {
            $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $fixture; $start.WorkingDirectory = $binary
            $start.UseShellExecute = $false; $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
            $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
            $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
            foreach ($argument in @($kernel, $caseName, $rawPath)) { $start.ArgumentList.Add($argument) }
            $process.StartInfo = $start
            if (-not $process.Start()) { throw 'The self-owned fixture child did not start.' }
            $row.started = $true; $row.pid = $process.Id
            # Copy directly to files so partial output survives an exception,
            # timeout, or a child that cannot be drained to completion.
            $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
            $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $row.timedOut = $true; $row.issues += "Timed out after $TimeoutSeconds seconds."
                # Kill only the exact Process object created immediately above.
                try { $process.Kill($true); $row.killed = $true }
                catch { $row.issues += "Could not terminate the timed-out fixture: $($_.Exception.Message)" }
                if (-not $process.WaitForExit(10000)) { $row.issues += 'Fixture still had no exit acknowledgement after termination.' }
            }
            if ($process.HasExited) { $row.exitCode = $process.ExitCode }
            else { $row.issues += 'No child exit code is available.' }
        } catch {
            $row.issues += $_.Exception.Message
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
        if ($jsonLines.Count -ne 1) { $row.issues += 'Expected exactly one child JSON result in stdout.' }
        else {
            try {
                $child = $jsonLines[0] | ConvertFrom-Json -ErrorAction Stop
                $row.child = $child
                if ($child.kind -ne 'native-engine-inline-fixture' -or $child.schemaVersion -ne 1 -or
                    $child.passed -ne $true -or $child.initialized -ne $true -or $child.failures -ne 0 -or $child.checks -le 0) {
                    $row.issues += 'The actual child result did not confirm successful checks.'
                }
                if ($child.gameRuntimeVerified -ne $false -or $child.productionAdmissionVerified -ne $false -or
                    $child.engineCancellationCompleted -ne $false) { $row.issues += 'Child result has an invalid verification boundary.' }
                $result.totalChecks += [long]$child.checks
            } catch { $row.issues += "Child JSON parse failed: $($_.Exception.Message)" }
        }
        if (Test-Path -LiteralPath $rawPath -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($rawPath)
            $row.raw = [ordered]@{ path = $rawPath; bytes = $bytes.Length; sha256 = Hash $rawPath }
            if ($bytes.Length -lt 16 -or [BitConverter]::ToUInt32($bytes, 0) -ne 1 -or
                [BitConverter]::ToUInt32($bytes, 4) -ne $bytes.Length -or [BitConverter]::ToUInt32($bytes, 8) -ne 432) {
                $row.issues += 'Raw receipt header/length/full-state ABI is invalid.'
            }
            if ($null -eq $row.child -or $row.child.rawBytes -ne $bytes.Length) { $row.issues += 'Raw receipt length does not match the child result.' }
        } else { $row.issues += 'No raw receipt was written.' }
        foreach ($inputRow in $inputRows) {
            if ((Hash $inputRow.path) -ne $inputRow.sha256) { $row.issues += "Build input changed during case: $($inputRow.name)" }
        }
        $row.stdoutSha256 = Hash $stdoutPath; $row.stderrSha256 = Hash $stderrPath
        $row.completedUtc = [DateTime]::UtcNow.ToString('o')
        $row.passed = $row.started -and -not $row.timedOut -and $row.exitCode -eq 0 -and $row.issues.Count -eq 0
        if (-not $row.passed) { $result.failures++ }
        WriteJson $row $casePath
        $result.cases += [ordered]@{ name = $caseName; passed = $row.passed; pid = $row.pid; exitCode = $row.exitCode
            timedOut = $row.timedOut; path = $casePath; sha256 = Hash $casePath; raw = $row.raw; issues = $row.issues }
        WriteJson $result $resultPath
        Write-Output "Inline fixture $caseName : PID=$($row.pid), exit=$($row.exitCode), passed=$($row.passed)"
    }
    $result.passed = $result.failures -eq 0 -and $result.cases.Count -eq $Cases.Count
} catch {
    $result.failure = $_.Exception.Message
    throw
} finally {
    foreach ($lock in $locks) { $lock.Dispose() }
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $result $resultPath
}
if (-not $result.passed) { throw "One or more self-owned fixture cases failed. Evidence was retained: $resultPath" }
Write-Output "Engine inline fixture cases: $resultPath"
