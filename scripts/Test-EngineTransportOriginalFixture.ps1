#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,60)][int]$TimeoutSeconds=60
)
$ErrorActionPreference='Stop'
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "Absolute path required: $Path" }
    [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
}
$binary=Absolute $BinaryDirectory;$output=Absolute $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
$manifestPath=Join-Path $binary 'transport-original-fixture-build.json'
$build=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($build.kind -ne 'engine-transport-original-fixture-build' -or $build.schemaVersion -ne 1 -or
    $build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0 -or $build.sourceHashesUnchanged -ne $true) {
    throw 'Use a successfully built owned original fixture.'
}
$fixture=Join-Path $binary 'EngineTransportOriginal.Fixture.exe'
$buildRow=@($build.files | Where-Object name -eq 'EngineTransportOriginal.Fixture.exe')
if ($buildRow.Count -ne 1 -or (Hash $fixture) -ne $buildRow[0].sha256 -or
    (Get-Item -LiteralPath $fixture).Length -ne $buildRow[0].bytes) { throw 'Fixture differs from the completed build.' }
$null=New-Item -ItemType Directory -Path $output
$stdoutPath=Join-Path $output 'stdout.log';$stderrPath=Join-Path $output 'stderr.log'
$childPath=Join-Path $output 'child-result.json';$rawPath=Join-Path $output 'receipt.bin'
$resultPath=Join-Path $output 'fixture-result.json'
$result=[ordered]@{
    schemaVersion=1;kind='engine-transport-original-fixture-run';passed=$false;started=$false
    pid=$null;exitCode=$null;timedOut=$false;killed=$false;checks=0;child=$null
    startedUtc=[DateTime]::UtcNow.ToString('o');executable=$fixture;arguments=@('--all',$childPath,$rawPath)
    buildManifest=[ordered]@{path=$manifestPath;sha256=Hash $manifestPath}
    input=[ordered]@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    runner=[ordered]@{path=$PSCommandPath;sha256=Hash $PSCommandPath}
    selfOwnedOnly=$true;gameExecuted=$false;sourcePatched=$false;productionAdmissionVerified=$false
    issues=@();files=@()
}
function Require([bool]$Condition,[string]$Reason) {
    $script:result.checks++
    if (-not $Condition) { throw $Reason }
}
WriteJson $result $resultPath
$process=[Diagnostics.Process]::new();$timer=[Diagnostics.Stopwatch]::StartNew()
$lock=$null;$stdoutFile=$null;$stderrFile=$null;$stdoutTask=$null;$stderrTask=$null
try {
    $lock=[IO.File]::Open($fixture,'Open','Read','Read')
    Require ((Hash $fixture) -eq $result.input.sha256) 'Fixture changed before its read lock.'
    $stdoutFile=[IO.File]::Open($stdoutPath,'CreateNew','Write','ReadWrite')
    $stderrFile=[IO.File]::Open($stderrPath,'CreateNew','Write','ReadWrite')
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$fixture;$start.WorkingDirectory=$binary;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach ($argument in $result.arguments) { $start.ArgumentList.Add($argument) }
    $process.StartInfo=$start
    if (-not $process.Start()) { throw 'Owned fixture process did not start.' }
    $result.started=$true;$result.pid=$process.Id
    $stdoutTask=$process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $stderrTask=$process.StandardError.BaseStream.CopyToAsync($stderrFile)
    if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
        $result.timedOut=$true
        $process.Kill();$result.killed=$true
        if (-not $process.WaitForExit(10000)) { throw 'Created child did not acknowledge termination.' }
    }
    $result.exitCode=$process.ExitCode
    $null=$stdoutTask.GetAwaiter().GetResult();$null=$stderrTask.GetAwaiter().GetResult()
    $stdoutFile.Dispose();$stdoutFile=$null;$stderrFile.Dispose();$stderrFile=$null
    Require (-not $result.timedOut -and $result.exitCode -eq 0) 'Owned fixture did not finish successfully.'
    Require ((Test-Path -LiteralPath $childPath -PathType Leaf) -and (Test-Path -LiteralPath $rawPath -PathType Leaf)) 'Actual child JSON/raw missing.'
    $childText=[IO.File]::ReadAllText($childPath)
    Require ([IO.File]::ReadAllText($stdoutPath).Trim() -ceq $childText.Trim()) 'Child stdout and persisted JSON differ.'
    $child=$childText | ConvertFrom-Json
    Require ($child.schemaVersion -eq 1 -and $child.kind -eq 'engine-transport-original-fixture' -and
        $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0 -and $child.fixedOriginals -eq 9) 'Child did not prove its exact successful fixture contract.'
    foreach ($name in @('callbackR11Verified','fullEaxVerified','flagsBranchesVerified','lastErrorVerified',
        'nvStackCanaryVerified','actualUnwindVerified','selfOwnedOnly')) { Require ($child.$name -eq $true) "Missing actual proof: $name" }
    foreach ($name in @('gameExecuted','productionAdmissionVerified','sourcePatched')) { Require ($child.$name -eq $false) "False verification boundary missing: $name" }
    $raw=[IO.File]::ReadAllBytes($rawPath)
    Require ($raw.Length -ge 128 -and [BitConverter]::ToUInt64($raw,0) -eq 0x0031524f545a4952UL -and
        [BitConverter]::ToUInt32($raw,8) -eq 1 -and [BitConverter]::ToUInt32($raw,12) -eq 128 -and
        [BitConverter]::ToUInt32($raw,16) -eq 256 -and [BitConverter]::ToUInt32($raw,20) -eq 256) 'Raw ABI header mismatch.'
    $rawChecks=[BitConverter]::ToUInt32($raw,24);$rawFailures=[BitConverter]::ToUInt32($raw,28)
    $callCount=[BitConverter]::ToUInt32($raw,32);$unwindCount=[BitConverter]::ToUInt32($raw,36)
    Require ($callCount -gt 0 -and $unwindCount -gt 0 -and
        $raw.LongLength -eq 128+256L*($callCount+$unwindCount) -and
        [BitConverter]::ToUInt64($raw,80) -eq $raw.LongLength -and $child.rawByteSize -eq $raw.LongLength -and
        $child.callCount -eq $callCount -and $child.unwindCount -eq $unwindCount) 'Raw record count/size differs from actual JSON.'
    Require ($rawChecks -eq $child.rawChecks -and $rawChecks -gt 0 -and $rawFailures -eq 0 -and $child.rawFailures -eq 0 -and
        $child.persistenceChecks -eq 3 -and $child.checks -eq $rawChecks+3 -and [BitConverter]::ToUInt32($raw,72) -eq 0) 'Raw/JSON assertion snapshots or persistence accounting differ.'
    Require ([BitConverter]::ToUInt32($raw,56) -eq 1 -and [BitConverter]::ToUInt32($raw,60) -eq 1 -and
        [BitConverter]::ToUInt32($raw,64) -eq 0 -and [BitConverter]::ToUInt32($raw,68) -eq 0 -and
        [BitConverter]::ToUInt32($raw,76) -eq 0) 'Raw actual-context/ownership boundaries differ.'
    for ($index=88;$index -lt 128;$index+=8) { Require ([BitConverter]::ToUInt64($raw,$index) -eq 0) 'Raw reserved bits are nonzero.' }
    Require ($child.actualCalls -eq $callCount -and $child.actualCalls -eq $child.baselineCalls+$child.originalCalls -and
        $child.baselineCalls -ge 9 -and $child.originalCalls -ge 9 -and
        [BitConverter]::ToUInt32($raw,40) -eq $child.baselineCalls -and [BitConverter]::ToUInt32($raw,44) -eq $child.originalCalls -and
        [BitConverter]::ToUInt32($raw,48) -eq $child.sehCaught -and [BitConverter]::ToUInt32($raw,52) -eq $child.cppCaught -and
        $child.sehCaught -gt 0 -and $child.cppCaught -gt 0) 'Actual baseline/original/exception counts disagree.'
    $callKinds=[Collections.Generic.HashSet[uint32]]::new();$windKinds=[Collections.Generic.HashSet[uint32]]::new()
    $callRoutes=[Collections.Generic.HashSet[string]]::new()
    $windPoints=[Collections.Generic.HashSet[string]]::new()
    $expectedOffsets=@(@(0,5,10,15),@(0,5,6,10,14),@(0,4,8,11,14),@(0,4,5,6,7,9,13,15),
        @(0,2,6,11,14),@(0,3,10,13,17),@(0,5,10,15),@(0,2,9,14),@(0,5,6,8,10,14))
    for ($index=0;$index -lt $callCount;++$index) {
        $at=128+256*$index;$kind=[BitConverter]::ToUInt32($raw,$at);$variant=[BitConverter]::ToUInt32($raw,$at+4)
        $branch=[BitConverter]::ToUInt32($raw,$at+8)
        Require ($kind -lt 9 -and $variant -lt 2 -and [BitConverter]::ToUInt32($raw,$at+20) -eq 1) 'Actual call record failed or has an unknown identity.'
        $null=$callKinds.Add($kind);$null=$callRoutes.Add("$kind/$variant/$branch")
    }
    for ($index=0;$index -lt $unwindCount;++$index) {
        $at=128+256*$callCount+256*$index;$kind=[BitConverter]::ToUInt32($raw,$at)
        $variant=[BitConverter]::ToUInt32($raw,$at+4);$branch=[BitConverter]::ToUInt32($raw,$at+8)
        $pointOffset=[BitConverter]::ToUInt32($raw,$at+12)
        Require ($kind -lt 9 -and [BitConverter]::ToUInt32($raw,$at+16) -eq 1 -and
            [BitConverter]::ToUInt32($raw,$at+20) -eq 1 -and [BitConverter]::ToUInt32($raw,$at+240) -eq 1 -and
            [BitConverter]::ToUInt64($raw,$at+56) -eq [BitConverter]::ToUInt64($raw,$at+80) -and
            [BitConverter]::ToUInt64($raw,$at+64) -eq [BitConverter]::ToUInt64($raw,$at+88)) 'A real Windows context did not unwind to its actual caller.'
        Require ($variant -lt 2 -and $branch -lt 2 -and $windPoints.Add("$kind/$variant/$branch/$pointOffset")) 'Actual instruction context is duplicated or has an unknown route.'
        $routeOffsets=@($expectedOffsets[$kind])
        if ($variant -eq 1) { $routeOffsets+= $routeOffsets[-1]+7 }
        Require ($pointOffset -in $routeOffsets) 'An unwind receipt is not at an exact prefix/MOV/JMP instruction boundary.'
        $null=$windKinds.Add($kind)
    }
    Require ($callKinds.Count -eq 9 -and $windKinds.Count -eq 9) 'Not all nine fixed shapes executed and unwound.'
    foreach ($kind in 0..8) { foreach ($variant in 0..1) { foreach ($branch in 0..1) {
        $routeOffsets=@($expectedOffsets[$kind])
        if ($variant -eq 1) { $routeOffsets+= $routeOffsets[-1]+7 }
        foreach ($pointOffset in $routeOffsets) {
            Require ($windPoints.Contains("$kind/$variant/$branch/$pointOffset")) 'An exact actual prefix/MOV/JMP context is missing.'
        }
    } } }
    foreach ($kind in @(2,4,5,7)) {
        foreach ($variant in @(0,1)) { foreach ($branch in @(0,1)) {
            Require ($callRoutes.Contains("$kind/$variant/$branch")) 'A flags/owner branch is missing its actual baseline or original call.'
        } }
    }
    Require ((Hash $fixture) -eq $result.input.sha256) 'Fixture bytes changed while testing.'
    $result.child=$child;$result.passed=$true
} catch {
    $result.issues+= $_.Exception.Message
    if ($result.started) {
        try { if (-not $process.HasExited) { $process.Kill();$result.killed=$true;$null=$process.WaitForExit(10000) } }
        catch { $result.issues+="Created child cleanup: $($_.Exception.Message)" }
    }
    throw
} finally {
    $timer.Stop();$result.elapsedMilliseconds=$timer.ElapsedMilliseconds
    foreach ($item in @(@{task=$stdoutTask;file=$stdoutFile},@{task=$stderrTask;file=$stderrFile})) {
        try { if ($null -ne $item.task) { $null=$item.task.GetAwaiter().GetResult() } } catch { $result.issues+=$_.Exception.Message;$result.passed=$false }
        if ($null -ne $item.file) { $item.file.Dispose() }
    }
    if ($null -ne $lock) { $lock.Dispose() }
    $process.Dispose()
    $result.files=@(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'fixture-result.json' |
        ForEach-Object { [ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length} })
    $result.completedUtc=[DateTime]::UtcNow.ToString('o');WriteJson $result $resultPath
}
if (-not $result.passed) { throw "Owned fixture failed; evidence retained: $resultPath" }
Write-Output "Transport original fixture: PID=$($result.pid), childChecks=$($result.child.checks), parentChecks=$($result.checks); $resultPath"
