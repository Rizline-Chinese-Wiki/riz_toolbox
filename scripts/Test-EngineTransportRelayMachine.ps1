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
$fixtureCpp=Join-Path $repo 'native/RizToolbox.EngineTransportRelayMachineFixture/RelayMachine.cpp'
$targetAsm=Join-Path $repo 'native/RizToolbox.EngineTransportRelayMachineFixture/RelayMachineTargets.asm'
$relaysCpp=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp'
$relaysHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.h'
$factsHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h'
$midHeader=Join-Path $repo 'native/RizToolbox.JudgeContext/MidContext.h'
$inlineAsm=Join-Path $repo 'native/RizToolbox.JudgeContext/EngineInlineProbeEntry.asm'
$schedulerAsm=Join-Path $repo 'native/RizToolbox.JudgeContext/EngineSchedulerEntry.asm'
$waitAsm=Join-Path $repo 'native/RizToolbox.JudgeContext/EngineWaitEntry.asm'
$sourcePaths=@($fixtureCpp,$targetAsm,$relaysCpp,$relaysHeader,$factsHeader,$midHeader,$inlineAsm,$schedulerAsm,$waitAsm,$PSCommandPath)
foreach ($path in $sourcePaths+@($unity,$vcvarsPath)) {
    $null=Absolute $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing relay-machine fixture input: $path" }
}
$manifest=[ordered]@{
    schemaVersion=1;kind='engine-transport-relay-machine-checks';passed=$false
    startedUtc=[DateTime]::UtcNow.ToString('o');completedUtc=$null
    buildExitCode=$null;buildSucceeded=$false;sourceHashesUnchanged=$false;gameDiskHashUnchanged=$false
    started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false;checks=0;childChecks=0
    child=$null;issues=@();sources=@();sourceAfter=@();files=@();inputArchive=$null
    fixtureExecuted=$false;relayCodeExecuted=$false;actualMachineContexts=$false
    gameExecuted=$false;productionAdmissionVerified=$false;installerExecuted=$false
    originalGameCodeExecuted=$false;kernelLedgerVerified=$false;exceptionPropagationVerified=$false
    privateMappingOnly=$true;sourceDiskPatched=$false;mappingOwnedLandingModified=$false
    routeCount=0;controlPairs=0;siteCount=0;chainSiteCount=0;partialSiteCount=0
    sourceTableRegistered=0;sourceTableDeleted=0;sourceRFCount=0;sourceContinuationsVerified=$false
    sourcePatchExecuted=$false;actualSourcePatchContextCount=0;mappingOwnedSourcePatchModified=$false;syntheticPartialPrimaryVerified=$false
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
    $archive=Join-Path $output 'relay-machine-fixture-sources.zip'
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
    $fixture=Join-Path $output 'EngineTransportRelayMachine.Fixture.exe'
    $compileUnits=@(
        [ordered]@{path=$fixtureCpp;name='RelayMachine'},
        [ordered]@{path=$relaysCpp;name='EngineTransportRelays'}
    )
    $asmUnits=@(
        [ordered]@{path=$targetAsm;name='RelayMachineTargets'},
        [ordered]@{path=$inlineAsm;name='EngineInlineProbeEntry'},
        [ordered]@{path=$schedulerAsm;name='EngineSchedulerEntry'},
        [ordered]@{path=$waitAsm;name='EngineWaitEntry'}
    )
    $commands=[Collections.Generic.List[string]]::new()
    foreach ($line in @('@echo off','setlocal DisableDelayedExpansion','chcp 65001 >nul',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1')) { $commands.Add($line) }
    foreach ($unit in $compileUnits) {
        $commands.Add("cl /nologo /std:c++17 /c /W4 /WX /O2 /EHa /MD /Zi /guard:cf /Fo`"$output\$($unit.name).obj`" /Fd`"$output\$($unit.name).compiler.pdb`" `"$($unit.path)`"")
        $commands.Add('if errorlevel 1 exit /b 1')
    }
    foreach ($unit in $asmUnits) {
        $commands.Add("ml64 /nologo /c /Zi /Fo`"$output\$($unit.name).obj`" /Fl`"$output\$($unit.name).lst`" `"$($unit.path)`"")
        $commands.Add('if errorlevel 1 exit /b 1')
    }
    $objectArguments=(@(($compileUnits+$asmUnits) | ForEach-Object { '"' + (Join-Path $output ($_.name+'.obj')) + '"' })) -join ' '
    $commands.Add("link /nologo /OUT:`"$fixture`" /PDB:`"$output\EngineTransportRelayMachine.Fixture.pdb`" /MAP:`"$output\EngineTransportRelayMachine.Fixture.map`" /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF $objectArguments bcrypt.lib kernel32.lib")
    $commands.Add('if errorlevel 1 exit /b 1')
    $commands.Add("dumpbin /nologo /headers /imports /unwindinfo /relocations `"$fixture`" > `"$output\fixture.pe.txt`"")
    $commands.Add('if errorlevel 1 exit /b 1');$commands.Add('exit /b 0')
    $commandPath=Join-Path $output 'build-relay-machine-fixture.cmd'
    [IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $buildExit=$LASTEXITCODE;$manifest.buildExitCode=$buildExit
    Require ($buildExit -eq 0) "Strict relay-machine fixture build failed: $buildExit"
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
    Require ($childProcess.Start()) 'Owned relay-machine fixture failed to start.'
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
    Require (-not $manifest.timedOut -and $manifest.exitCode -eq 0) 'Actual owned relay-machine fixture failed or timed out.'
    Require (Test-Path -LiteralPath $childPath -PathType Leaf) 'Actual owned child JSON is missing.'
    $childText=[IO.File]::ReadAllText($childPath)
    Require ([IO.File]::ReadAllText($stdoutPath).Trim() -ceq $childText.Trim()) 'Actual child stdout and persisted JSON differ.'
    $child=$childText | ConvertFrom-Json
    Require ($child.schemaVersion -eq 1 -and $child.kind -eq 'engine-transport-relay-machine' -and
        $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0 -and $child.pid -eq $manifest.pid) 'Child success/schema/PID is not the actual invoked machine fixture.'
    Require ($child.fixedDiskSha256 -ceq $manifest.unityDisk.expectedSha256 -and $child.fatal -ceq '') 'Actual child did not finish against the locked fixed game disk hash.'
    foreach ($name in @('fixtureExecuted','relayCodeExecuted','actualMachineContexts','actualReturnPcUnwind','dispatchPoisonVerified',
        'expandedBypassVerified','allStateControlPairs','sourceDiskUnchanged','privateMappingOnly','mappingOwnedLandingModified',
        'planArenaExtendedForControl','actualEntrySavedTfNaturallyRestored','sourceContinuationsVerified',
        'sourcePatchExecuted','mappingOwnedSourcePatchModified','syntheticPartialPrimaryVerified')) {
        Require ($child.$name -eq $true) "Actual machine proof is missing: $name"
    }
    foreach ($name in @('gameExecuted','productionAdmissionVerified','installerExecuted','originalGameCodeExecuted','kernelLedgerVerified','exceptionPropagationVerified','cppDispatcherSingleStepped')) {
        Require ($child.$name -eq $false) "Owned relay-machine boundary is missing: $name"
    }
    Require ($child.routeCount -eq 31 -and @($child.routes).Count -eq 31 -and $child.controlPairs -eq 31 -and
        $child.siteCount -eq 23 -and $child.chainSiteCount -eq 19 -and $child.partialSiteCount -eq 4 -and $child.actualContextCount -gt 0 -and
        $child.registeredTableCount -eq 31 -and $child.deletedTableCount -eq 31 -and $child.actualProbeWalks -eq 28 -and
        $child.naturalTfResumptions -eq 28 -and $child.sourceTableRegistered -eq 31 -and $child.sourceTableDeleted -eq 31 -and
        $child.sourceRFCount -eq 27 -and $child.actualSourcePatchContextCount -eq 62) 'The fixed routes/controls, source unwind mix, actual contexts or registered-table lifetime evidence is incomplete.'
    $routes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sites=[Collections.Generic.HashSet[uint32]]::new()
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $branchSites=@(3,6,9,11,14,17,19,21)
    $expectedNv=@('1122334455667788','2233445566778899','33445566778899aa','445566778899aabb',
        '5566778899aabbcc','66778899aabbccdd','778899aabbccddee','8899aabbccddeeff') |
        ForEach-Object { [Convert]::ToUInt64($_,16) }
    [uint64]$contexts=0;[uint32]$poisoned=0;[uint32]$bypassed=0;[uint32]$probeWalks=0;[uint32]$sourcePatchContexts=0
    foreach ($route in $child.routes) {
        Require ($route.site -ge 0 -and $route.site -lt 23 -and $route.branch -in @(0,1) -and
            $routes.Add("$($route.site)/$($route.branch)") -and -not [string]::IsNullOrWhiteSpace($route.name) -and
            $names.Add($route.name) -and $route.passed -eq $true) 'An actual machine route is missing, duplicated or failed.'
        $null=$sites.Add([uint32]$route.site)
        $expectedProbe=if ($route.site -in @(9,11,14) -and $route.branch -eq 0) { 0 } else { 1 }
        Require ($route.controlProbeCount -eq 0 -and $route.expectedProbeCount -eq $expectedProbe -and
            $route.relayProbeCount -eq $expectedProbe -and $route.actualContextCount -gt 0 -and
            $route.stateEqual -eq $true -and $route.memoryEqual -eq $true -and
            $route.controlComplete -eq $true -and $route.relayComplete -eq $true -and
            $route.lastErrorPreserved -eq $true) 'Control and relay actual machine state/memory/observation, LastError or completion disagree.'
        Require ($route.returnPcUnwind -eq ($expectedProbe -ne 0)) 'Only actual probe-return routes may claim actual return-PC unwind.'
        Require ($route.sourceContinuationsVerified -eq $true -and $route.sourceRfLookups -ge 81) 'Actual source function-table/continuation lookups were not completed for this owned route.'
        Require ($route.sourcePatchPc -gt 0 -and $route.sourcePatchContextCount -eq 2 -and @($route.sourcePatchTargets).Count -eq 2 -and
            @($route.sourcePatchCounts).Count -eq 2 -and $route.sourcePatchCounts[0] -eq 1 -and $route.sourcePatchCounts[1] -eq 1 -and
            @($route.sourcePatchUnwind).Count -eq 2 -and $route.sourcePatchUnwind[0] -eq $true -and $route.sourcePatchUnwind[1] -eq $true) 'Each control/relay variant must execute and unwind its actual owned source E9 exactly once.'
        Require ($route.poisonVerified -eq ($route.site -in @(17,18,19,20))) 'Only the actual Dispatch routes may prove the late-RBX poison case.'
        if ($route.poisonVerified) { ++$poisoned }
        if ($expectedProbe -eq 0) { ++$bypassed }
        Require ($route.requiredContextCount -gt 0 -and $route.requiredContextCount -eq @($route.requiredPoints).Count -and
            $route.requiredContextCount -eq $route.observedContextCount -and $route.actualContextCount -eq @($route.points).Count -and
            $route.actualContextCount -eq $route.requiredContextCount+$expectedProbe+2) 'Actual source E9/relay TF points and real stackwalk receipts are not the exact required route evidence.'
        $required=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $sourceBases=[Collections.Generic.Dictionary[uint32,uint64]]::new()
        foreach ($point in $route.requiredPoints) {
            $key="$($point.variant)/$($point.offset)"
            Require ($point.variant -in @(0,1) -and $point.offset -ge 0 -and $point.offset -lt 160 -and
                $point.pc -gt $point.offset -and -not $required.ContainsKey($key)) 'Required native instruction PC has an invalid identity or duplicate offset.'
            $required.Add($key,$point)
            [uint64]$sourceBase=[uint64]$point.pc-[uint64]$point.offset
            if ($sourceBases.ContainsKey([uint32]$point.variant)) {
                Require ($sourceBases[[uint32]$point.variant] -eq $sourceBase) 'Required instruction PCs disagree about their actual control/relay base.'
            } else { $sourceBases.Add([uint32]$point.variant,$sourceBase) }
        }
        Require ($sourceBases.Count -eq 2) 'Both actual control and relay instruction scopes must have required points.'
        foreach ($variant in @(0,1)) {
            Require ([uint64]$route.sourcePatchTargets[$variant] -eq $sourceBases[[uint32]$variant]) 'The actual source E9 target differs from the selected control or relay instruction scope.'
        }
        $observed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $sourcePatchObserved=[Collections.Generic.HashSet[uint32]]::new()
        [uint32]$walksForRoute=0
        foreach ($point in $route.points) {
            $key="$($point.variant)/$($point.offset)"
            Require ($point.actualContext -eq $true -and $point.passed -eq $true -and
                $point.variant -in @(0,1) -and $point.source -in @('single-step','actual-probe-stackwalk','actual-source-patch') -and
                $point.expectedRip -gt 0 -and $point.expectedRsp -gt 0 -and $point.beforeRsp -gt 0 -and
                $point.restoredRip -eq $point.expectedRip -and $point.restoredRsp -eq $point.expectedRsp -and
                $point.nonvolatilesRestored -eq $true -and $point.xmmNonvolatilesRestored -eq $true -and
                @($point.restoredNv).Count -eq 8) 'An actual Windows CONTEXT/stackwalk did not restore the exact caller or NV registers.'
            for ($nv=0;$nv -lt 8;++$nv) {
                Require ([uint64]$point.restoredNv[$nv] -eq $expectedNv[$nv]) 'The actual restored nonvolatile value differs from its independently seeded caller register.'
            }
            if ($point.source -eq 'actual-source-patch') {
                Require ($sourcePatchObserved.Add([uint32]$point.variant) -and $point.offset -eq 0 -and $point.pc -eq $route.sourcePatchPc -and
                    $point.sourceRfMatched -eq $true -and $point.imageBase -gt 0 -and $point.functionAddress -gt 0 -and
                    $point.functionBegin -lt $point.functionEnd -and $point.functionUnwind -gt 0 -and
                    [uint64]$point.pc -ge ([uint64]$point.imageBase+[uint64]$point.functionBegin) -and
                    [uint64]$point.pc -lt ([uint64]$point.imageBase+[uint64]$point.functionEnd)) 'Actual source E9 CONTEXT did not resolve and unwind its exact original source RF.'
                ++$sourcePatchContexts
                continue
            }
            Require ($required.ContainsKey($key) -and $point.pc -eq $required[$key].pc) 'An actual relay instruction CONTEXT/stackwalk PC differs from its independently required route instruction.'
            if ($point.source -eq 'single-step') {
                Require ($observed.Add($key)) 'An actual TF instruction receipt is duplicated.'
            } else {
                Require ($point.variant -eq 1 -and $expectedProbe -eq 1) 'A control or expanded-zero route cannot borrow a probe stackwalk receipt.'
                ++$walksForRoute;++$probeWalks
            }
        }
        Require ($observed.Count -eq $required.Count -and $walksForRoute -eq $expectedProbe) 'A required actual instruction CONTEXT or actual probe return-PC stackwalk is missing.'
        Require ($sourcePatchObserved.Count -eq 2) 'The route is missing one of its two actual source E9 CONTEXTs.'
        $contexts += [uint64]$route.actualContextCount
    }
    Require ($sites.Count -eq 23 -and $routes.Count -eq 31 -and $poisoned -eq 6 -and $bypassed -eq 3) 'Full site, Dispatch branch or expanded-bypass route coverage is incomplete.'
    foreach ($site in 0..22) {
        if ($site -in $branchSites) {
            foreach ($branch in @(0,1)) { Require ($routes.Contains("$site/$branch")) 'A real zero/nonzero conditional machine path is missing.' }
        } else {
            Require ($routes.Contains("$site/0") -or $routes.Contains("$site/1")) 'A real nonconditional machine path is missing.'
        }
    }
    Require ($contexts -eq $child.actualContextCount -and $probeWalks -eq $child.actualProbeWalks -and
        $sourcePatchContexts -eq 62 -and $sourcePatchContexts -eq $child.actualSourcePatchContextCount) 'Actual per-route source E9/relay CONTEXT and stackwalk receipts do not sum to the persisted native counts.'
    Require ((Hash $fixture) -eq $manifest.executable.sha256) 'Owned executable changed during its actual run.'
    $manifest.child=$child;$manifest.childChecks=$child.checks
    $manifest.fixtureExecuted=$true;$manifest.relayCodeExecuted=$true;$manifest.actualMachineContexts=$true
    $manifest.mappingOwnedLandingModified=$true
    $manifest.routeCount=$child.routeCount;$manifest.controlPairs=$child.controlPairs;$manifest.siteCount=$child.siteCount
    $manifest.chainSiteCount=$child.chainSiteCount;$manifest.partialSiteCount=$child.partialSiteCount
    $manifest.actualContextCount=$child.actualContextCount;$manifest.actualProbeWalks=$child.actualProbeWalks
    $manifest.naturalTfResumptions=$child.naturalTfResumptions
    $manifest.sourceTableRegistered=$child.sourceTableRegistered;$manifest.sourceTableDeleted=$child.sourceTableDeleted
    $manifest.sourceRFCount=$child.sourceRFCount;$manifest.sourceContinuationsVerified=$child.sourceContinuationsVerified
    $manifest.sourcePatchExecuted=$child.sourcePatchExecuted;$manifest.actualSourcePatchContextCount=$child.actualSourcePatchContextCount
    $manifest.mappingOwnedSourcePatchModified=$child.mappingOwnedSourcePatchModified;$manifest.syntheticPartialPrimaryVerified=$child.syntheticPartialPrimaryVerified
    $manifest.passed=$true
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
        $manifest.sourceHashesUnchanged=$manifest.sources.Count -eq 10
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
if (-not $manifest.passed) { throw "Owned relay-machine fixture failed; evidence retained: $resultPath" }
Write-Output "Relay machine: PID=$($manifest.pid), 31 actual route/control pairs, childChecks=$($manifest.childChecks), parentChecks=$($manifest.checks); $resultPath"
