#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$UnityPath='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll',
    [string]$VcVars = '',
    [ValidateRange(1,60)][int]$TimeoutSeconds=60
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "Absolute path required: $Path" }
    $full=[IO.Path]::GetFullPath($Path)
    if ($full -match '[\r\n"%&|<>^]') { throw 'Unsupported command path characters.' }
    $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
}
$output=Absolute $OutputDirectory;$unity=Absolute $UnityPath;$vcvarsPath=Absolute $VcVars
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
$fixtureCpp=Join-Path $repo 'native/RizToolbox.EngineTransportSourceWriteFixture/SourceWrites.cpp'
$sourceCpp=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.cpp'
$sourceHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h'
$relaysCpp=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp'
$relaysHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.h'
$factsHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h'
$publicationHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportPublication.h'
$sourcePaths=@($fixtureCpp,$sourceCpp,$sourceHeader,$relaysCpp,$relaysHeader,$factsHeader,$publicationHeader,$PSCommandPath)
foreach ($path in $sourcePaths+@($unity,$vcvarsPath)) {
    $null=Absolute $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing source-write fixture input: $path" }
}
$manifest=[ordered]@{
    schemaVersion=1;kind='engine-transport-source-writes-checks';passed=$false
    startedUtc=[DateTime]::UtcNow.ToString('o');completedUtc=$null
    buildExitCode=$null;buildSucceeded=$false;sourceHashesUnchanged=$false;gameDiskHashUnchanged=$false
    started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false;checks=0;childChecks=0
    child=$null;issues=@();sources=@();sourceAfter=@();files=@();inputArchive=$null
    fixtureExecuted=$false;actualSourceWrites=$false;privateMappingOnly=$true
    mappedImageNeverExecuted=$true;relayCodeExecuted=$false;gameExecuted=$false;mappedImageExecuted=$false
    installerExecuted=$false;productionAdmissionVerified=$false;productionInstallerExecuted=$false;sourceDiskPatched=$false
    simulatedStageFailures=$true;actualOsFailureClaims=$false
    normalCases=0;stageFailureCases=0;preflightCases=0;registeredTablesDeleted=$false
    outputDirectory=$output;compilerEnvironment=$vcvarsPath;timeoutSeconds=$TimeoutSeconds
    unityDisk=[ordered]@{path=$unity;sha256=$null;expectedSha256=$null;bytes=$null;readOnly=$true}
}
function Require([bool]$Condition,[string]$Reason) {
    $script:manifest.checks++
    if (-not $Condition) { throw $Reason }
}
$null=New-Item -ItemType Directory -Path $output
$resultPath=Join-Path $output 'test-result.json'
$locks=[Collections.Generic.List[IDisposable]]::new()
$childProcess=[Diagnostics.Process]::new()
$stdoutFile=$null;$stderrFile=$null;$stdoutTask=$null;$stderrTask=$null
$timer=[Diagnostics.Stopwatch]::StartNew()
try {
    foreach ($path in $sourcePaths+@($unity)) { $locks.Add([IO.File]::Open($path,'Open','Read','Read')) }
    $manifest.sources=@($sourcePaths | ForEach-Object {
        [ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}
    })
    $factMatches=[regex]::Matches([IO.File]::ReadAllText($factsHeader),'(?m)^inline constexpr char UnitySha256\[\]\s*=\s*"([a-fA-F0-9]{64})";')
    Require ($factMatches.Count -eq 1) 'Fixed facts must identify exactly one actual Unity disk SHA256.'
    $manifest.unityDisk.expectedSha256=$factMatches[0].Groups[1].Value.ToLowerInvariant()
    $manifest.unityDisk.sha256=Hash $unity;$manifest.unityDisk.bytes=(Get-Item -LiteralPath $unity).Length
    Require ($manifest.unityDisk.sha256 -eq $manifest.unityDisk.expectedSha256) 'Game disk SHA differs from generated fixed facts.'
    WriteJson (@($manifest.sources)+@($manifest.unityDisk)) (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'source-write-fixture-sources.zip'
    $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($row in $manifest.sources) {
            $relative=[IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/')
            if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) { throw 'Fixture source ZIP must stay within its repository.' }
            $entry=$zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $destination=$entry.Open();$source=[IO.File]::OpenRead($row.path)
            try { $source.CopyTo($destination) } finally { $source.Dispose();$destination.Dispose() }
            Require ((Hash $row.path) -eq $row.sha256) "Source changed while archiving: $($row.path)"
        }
    } finally { $zip.Dispose() }
    $manifest.inputArchive=[ordered]@{path=$archive;sha256=Hash $archive;entries=$manifest.sources.Count;gameIncluded=$false}
    $fixture=Join-Path $output 'EngineTransportSourceWrites.Fixture.exe'
    $commands=[Collections.Generic.List[string]]::new()
    foreach ($line in @('@echo off','setlocal DisableDelayedExpansion','chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1')) { $commands.Add($line) }
    $units=@(
        [ordered]@{path=$fixtureCpp;name='SourceWriteFixture'},
        [ordered]@{path=$sourceCpp;name='EngineTransportSourceWrites'},
        [ordered]@{path=$relaysCpp;name='EngineTransportRelays'}
    )
    foreach ($unit in $units) {
        # Fault injection exists only in this independent executable. No
        # production build, source target or gate is recompiled by this runner.
        $commands.Add("cl /nologo /std:c++17 /c /W4 /WX /O2 /EHa /MD /Zi /guard:cf /DRIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE /Fo`"$output\$($unit.name).obj`" /Fd`"$output\$($unit.name).compiler.pdb`" `"$($unit.path)`"")
        $commands.Add('if errorlevel 1 exit /b 1')
    }
    $commands.Add("link /nologo /OUT:`"$fixture`" /PDB:`"$output\EngineTransportSourceWrites.Fixture.pdb`" /MAP:`"$output\EngineTransportSourceWrites.Fixture.map`" /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$output\SourceWriteFixture.obj`" `"$output\EngineTransportSourceWrites.obj`" `"$output\EngineTransportRelays.obj`" bcrypt.lib kernel32.lib")
    $commands.Add('if errorlevel 1 exit /b 1')
    $commands.Add("dumpbin /nologo /headers /imports /unwindinfo /relocations `"$fixture`" > `"$output\fixture.pe.txt`"")
    $commands.Add('if errorlevel 1 exit /b 1');$commands.Add('exit /b 0')
    $commandPath=Join-Path $output 'build-source-write-fixture.cmd'
    [IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $buildExit=$LASTEXITCODE;$manifest.buildExitCode=$buildExit
    Require ($buildExit -eq 0) "Strict source-write fixture build failed: $buildExit"
    $manifest.buildSucceeded=$true
    $locks.Add([IO.File]::Open($fixture,'Open','Read','Read'))
    $manifest.executable=[ordered]@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    $childPath=Join-Path $output 'child-result.json'
    $stdoutPath=Join-Path $output 'stdout.log';$stderrPath=Join-Path $output 'stderr.log'
    $manifest.arguments=@('--all',$unity,$childPath)
    WriteJson $manifest $resultPath
    $stdoutFile=[IO.File]::Open($stdoutPath,'CreateNew','Write','ReadWrite')
    $stderrFile=[IO.File]::Open($stderrPath,'CreateNew','Write','ReadWrite')
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$fixture;$start.WorkingDirectory=$output;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach ($argument in $manifest.arguments) { $start.ArgumentList.Add($argument) }
    $childProcess.StartInfo=$start
    Require ($childProcess.Start()) 'Owned source-write fixture failed to start.'
    $manifest.started=$true;$manifest.fixtureExecuted=$true;$manifest.pid=$childProcess.Id
    $stdoutTask=$childProcess.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $stderrTask=$childProcess.StandardError.BaseStream.CopyToAsync($stderrFile)
    if (-not $childProcess.WaitForExit($TimeoutSeconds*1000)) {
        $manifest.timedOut=$true;$childProcess.Kill();$manifest.killed=$true
        Require ($childProcess.WaitForExit(10000)) 'Created child did not acknowledge termination.'
    }
    $manifest.exitCode=$childProcess.ExitCode
    $null=$stdoutTask.GetAwaiter().GetResult();$null=$stderrTask.GetAwaiter().GetResult()
    $stdoutFile.Dispose();$stdoutFile=$null;$stderrFile.Dispose();$stderrFile=$null
    Require (-not $manifest.timedOut -and $manifest.exitCode -eq 0) 'Actual owned source-write fixture failed or timed out.'
    Require (Test-Path -LiteralPath $childPath -PathType Leaf) 'Actual owned child JSON is missing.'
    $childText=[IO.File]::ReadAllText($childPath)
    Require ([IO.File]::ReadAllText($stdoutPath).Trim() -ceq $childText.Trim()) 'Actual child stdout and persisted JSON differ.'
    $child=$childText | ConvertFrom-Json
    Require ($child.schemaVersion -eq 1 -and $child.kind -eq 'engine-transport-source-writes' -and
        $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0 -and $child.pid -eq $manifest.pid) 'Child success/schema/PID is not the actual invoked fixture.'
    Require ($child.fixedDiskSha256 -ceq $manifest.unityDisk.expectedSha256 -and $child.fatal -ceq '') 'Actual child did not complete against the same frozen game disk hash.'
    foreach ($name in @('fixtureExecuted','actualSourceWrites','privateMappingOnly','mappedImageNeverExecuted',
        'registeredTablesDeleted','sourceDiskUnchanged','simulatedStageFailures')) {
        Require ($child.$name -eq $true) "Actual owned source-write proof is missing: $name"
    }
    foreach ($name in @('relayCodeExecuted','gameExecuted','mappedImageExecuted','installerExecuted',
        'productionAdmissionVerified','productionInstallerExecuted','sourceDiskPatched','actualOsFailureClaims')) {
        Require ($child.$name -eq $false) "Owned injected-failure verification boundary is missing: $name"
    }
    Require ($child.caseCount -eq 226 -and @($child.cases).Count -eq 226 -and $child.normalCases -eq 1 -and
        $child.stageFailureCases -eq 216 -and $child.preflightCases -eq 9) 'The complete 1+216+9 case matrix did not execute.'
    Require ($child.registeredTableCount -eq 225 -and $child.deletedTableCount -eq 225) 'Every one of the 225 actual table registrations must be deleted before owned memory is released.'
    Require ($child.normal.passed -eq $true -and $child.normal.sourceWriteCompletions -eq 36 -and
        $child.normal.committedWindows -eq 68719476735UL -and $child.normal.outstandingProtection -eq 0) 'The actual normal 36-window commit did not complete every obligation.'
    $caseNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $stageRoutes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $preflights=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $normalCount=0;[uint64]$totalWritten=0
    $preflightNames=@('bad-request','bad-plan','bad-source','production-gate','table-missing','source-not-RX','nonfresh-receipt',
        'coherent-relay-tamper','returnpc-at-rf-end')
    foreach ($case in $child.cases) {
        Require (-not [string]::IsNullOrWhiteSpace($case.name) -and $caseNames.Add($case.name) -and
            $case.passed -eq $true -and $case.checks -gt 0 -and $case.result -eq $case.expected) 'An actual source-write case failed or has a duplicate/missing identity.'
        foreach ($proof in @('terminalReceiptUnchanged','lastErrorPreserved','tableDeleted','sourceBytesVerified','protectionsVerified','eventsVerified')) {
            Require ($case.$proof -eq $true) "Actual per-case responsibility proof missing: $($case.name)/$proof"
        }
        Require ($case.outstandingProtection -eq 0) "Actual source protections remain outstanding: $($case.name)"
        Require ($case.fixtureResetPreflightProtection -eq ($case.name -eq 'source-not-RX') -and
            $case.simulatedStageFailure -eq ($case.faultStage -ne 0) -and
            $case.tableRegistered -eq ($case.name -ne 'table-missing')) 'Fixture cleanup or injected-stage scope must not be credited as a real writer/OS failure.'
        Require ($case.actualNearMatchesPlan -eq $true -and
            $case.coherentRelayTamper -eq ($case.name -eq 'coherent-relay-tamper') -and
            $case.returnPcAtRfEnd -eq ($case.name -eq 'returnpc-at-rf-end')) 'Actual coherent near-memory/Plan and RF-end negative proofs must match their exact cases.'
        if ($case.name -eq 'normal') {
            ++$normalCount
            Require ($case.profile -eq 1 -and $case.committedCount -eq 36 -and $case.sourceWriteAttempts -eq 36 -and
                $case.sourceWriteCompletions -eq 36 -and $case.actualPrivateSourceWrites -eq $true -and $case.tableRegistered -eq $true) 'The normal case does not prove actual private writes and registered relays.'
        } elseif ($case.name -in $preflightNames) {
            Require ($preflights.Add($case.name)) 'A preflight route was duplicated.'
            Require ($case.sourceWriteAttempts -eq 0 -and $case.sourceWriteCompletions -eq 0 -and
                $case.committedCount -eq 0 -and $case.sourceBytesWritten -eq 0 -and
                $case.actualPrivateSourceWrites -eq $false) 'Every rejected preflight must retain exactly zero source-write responsibility.'
            if ($case.name -in @('coherent-relay-tamper','returnpc-at-rf-end')) {
                Require ($case.result -eq 3 -and $case.expected -eq 3 -and $case.tableRegistered -eq $true -and
                    $case.actualNearMatchesPlan -eq $true) 'A coherent tampered relay or RF-end return PC must be rejected as BadPlan before any source write.'
            }
        } else {
            Require ($case.faultStage -in @(2,3,4,6,7,8) -and $case.faultWindow -ge 0 -and $case.faultWindow -lt 36 -and
                $stageRoutes.Add("$($case.faultStage)/$($case.faultWindow)") -and $case.profile -eq 1 -and $case.tableRegistered -eq $true) 'An injected stage/window route is missing, duplicated, or not owned.'
            Require ($case.committedCount -eq $case.faultWindow -and $case.sourceWriteCompletions -eq $case.faultWindow) 'A stage failure lost the exact committed-prefix responsibility.'
        }
        # Independently recount every persisted physical window. The native
        # fixture separately compares the entire private image against exact
        # expected full/partial/damaged writes, including bytes outside windows.
        Require (@($case.windows).Count -eq 36) 'Every case must persist all 36 actual source-window receipts.'
        $windowIndices=[Collections.Generic.HashSet[uint32]]::new();[uint64]$caseWritten=0
        foreach ($window in $case.windows) {
            Require ($window.index -ge 0 -and $window.index -lt 36 -and $windowIndices.Add([uint32]$window.index) -and
                $window.actualBytes -cmatch '^(?:[0-9a-f]{2}){5,32}$') 'A physical window receipt is missing, duplicated, or lacks bounded actual bytes.'
            $physicalBytes=$window.actualBytes.Length/2
            Require ($window.written -ge 0 -and $window.written -le $physicalBytes) 'Written-byte responsibility exceeds its actual physical source window.'
            $caseWritten += [uint64]$window.written
            if ($case.name -eq 'normal' -or $case.faultStage -ne 0) {
                $previous=$case.name -eq 'normal' -or $window.index -lt $case.faultWindow
                $target=$case.faultStage -ne 0 -and $window.index -eq $case.faultWindow
                $writesTarget=$target -and $case.faultStage -notin @(2,3)
                $expectedWritten=if ($previous) { $physicalBytes } elseif ($writesTarget) {
                    if ($case.faultStage -eq 4) { 1 } else { $physicalBytes }
                } else { 0 }
                $protectedTarget=$target -and $case.faultStage -ne 2
                Require ($window.written -eq $expectedWritten -and
                    $window.protectAttempts -eq [int]($previous -or $target) -and
                    $window.protectSucceeded -eq [int]($previous -or $protectedTarget) -and
                    $window.restoreSucceeded -eq [int]($previous -or $protectedTarget) -and
                    $window.flushAttempts -eq [int]($previous -or ($target -and $case.faultStage -eq 8)) -and
                    $window.flushSucceeded -eq [int]$previous) 'Actual per-window progress differs from the exact completed-prefix/injected-stage matrix.'
            } else {
                Require ($window.written -eq 0 -and $window.protectAttempts -eq 0 -and $window.protectSucceeded -eq 0 -and
                    $window.restoreSucceeded -eq 0 -and $window.flushAttempts -eq 0 -and $window.flushSucceeded -eq 0) 'A rejected preflight cannot report source writes or protection/flush operations.'
            }
        }
        Require ($windowIndices.Count -eq 36 -and $caseWritten -eq $case.sourceBytesWritten -and
            $case.actualPrivateSourceWrites -eq ($caseWritten -ne 0)) 'Per-window byte totals disagree with actual case write responsibility.'
        $totalWritten += $caseWritten
    }
    Require ($normalCount -eq 1 -and $stageRoutes.Count -eq 216 -and $preflights.Count -eq 9) 'Normal/stage/preflight case partition is incomplete.'
    foreach ($stage in @(2,3,4,6,7,8)) { foreach ($window in 0..35) {
        Require ($stageRoutes.Contains("$stage/$window")) 'An actual injected failure route is missing.'
    } }
    Require ($totalWritten -eq $child.sourceBytesWritten) 'Actual total source bytes disagree across all 226 cases.'
    Require ((Hash $fixture) -eq $manifest.executable.sha256) 'Owned executable changed during its actual run.'
    $manifest.child=$child;$manifest.childChecks=$child.checks;$manifest.actualSourceWrites=$true
    $manifest.normalCases=1;$manifest.stageFailureCases=216;$manifest.preflightCases=9
    $manifest.registeredTablesDeleted=$true;$manifest.passed=$true
} catch {
    $manifest.passed=$false;$manifest.issues+=$_.Exception.Message
    if ($manifest.started) {
        try {
            if (-not $childProcess.HasExited) {
                $childProcess.Kill();$manifest.killed=$true
                $null=$childProcess.WaitForExit(10000)
            }
        } catch { $manifest.issues+="Created child cleanup: $($_.Exception.Message)" }
    }
} finally {
    foreach ($item in @(@{task=$stdoutTask;file=$stdoutFile},@{task=$stderrTask;file=$stderrFile})) {
        try {
            if ($null -ne $item.task) {
                if (-not $item.task.Wait(10000)) { throw 'Created child output stream did not close within cleanup timeout.' }
                $null=$item.task.GetAwaiter().GetResult()
            }
        } catch { $manifest.issues+=$_.Exception.Message;$manifest.passed=$false }
        if ($null -ne $item.file) { $item.file.Dispose() }
    }
    try {
        $manifest.sourceAfter=@($sourcePaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length} })
        WriteJson $manifest.sourceAfter (Join-Path $output 'source-after.json')
        foreach ($row in $manifest.sources) {
            if ((Hash $row.path) -ne $row.sha256) { throw "Source changed: $($row.path)" }
        }
        $manifest.sourceHashesUnchanged=$manifest.sources.Count -eq 8
        $manifest.unityDiskAfter=[ordered]@{path=$unity;sha256=Hash $unity;bytes=(Get-Item -LiteralPath $unity).Length}
        WriteJson $manifest.unityDiskAfter (Join-Path $output 'unity-disk-after.json')
        $manifest.gameDiskHashUnchanged=$manifest.unityDiskAfter.sha256 -eq $manifest.unityDisk.sha256 -and
            $manifest.unityDiskAfter.sha256 -eq $manifest.unityDisk.expectedSha256 -and
            $manifest.unityDiskAfter.bytes -eq $manifest.unityDisk.bytes
        if (-not $manifest.sourceHashesUnchanged -or -not $manifest.gameDiskHashUnchanged) { throw 'Source/game disk integrity is incomplete.' }
    } catch { $manifest.passed=$false;$manifest.issues+=$_.Exception.Message }
    foreach ($item in $locks) { $item.Dispose() }
    $childProcess.Dispose();$timer.Stop();$manifest.elapsedMilliseconds=$timer.ElapsedMilliseconds
    $manifest.files=@(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'test-result.json' |
        ForEach-Object { [ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length} })
    $manifest.completedUtc=[DateTime]::UtcNow.ToString('o');WriteJson $manifest $resultPath
}
if (-not $manifest.passed) { throw "Owned source-write fixture failed; evidence retained: $resultPath" }
Write-Output "Source writes: PID=$($manifest.pid), 226 actual cases, childChecks=$($manifest.childChecks), parentChecks=$($manifest.checks); $resultPath"
