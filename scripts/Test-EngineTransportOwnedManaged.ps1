#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Python = '',
    [string]$Dotnet = '',
    [string]$HostFxr = '',
    [ValidateRange(1,300)][int]$TimeoutSeconds=180
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }
if (-not $Dotnet) { $Dotnet = Get-RizDotnetPath }
if (-not $HostFxr) { $HostFxr = Get-RizHostFxrPath -DotnetPath $Dotnet }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'};[IO.Path]::GetFullPath($Path)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$candidate=Absolute $CandidateDirectory;$output=Absolute $OutputDirectory;$hostFxrPath=Absolute $HostFxr
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent result directory.'}
$buildPath=Join-Path $candidate 'owned-candidate-build.json';$build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
if($build.kind -ne 'engine-transport-owned-candidate-build' -or $build.passed -ne $true -or $build.managedHost -ne $true){throw 'An actual successful managed-host owned V5 candidate build is required.'}
$profileSource='C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs'
$generator=Join-Path $PSScriptRoot 'generate-engine-transport-managed-profile.py'
$null=New-Item -ItemType Directory -Path $output
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-owned-managed-checks';passed=$false;buildExitCode=$null
    inputs=@();inputHashesUnchanged=$false;facts=$null;children=@();failure=$null;childTimeoutSeconds=$TimeoutSeconds
    actualNativeClrFullAdoption=$false;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false}
$locks=[Collections.Generic.List[IDisposable]]::new();$oldLocation=Get-Location
try{
    Set-Location -LiteralPath $repo
    # Freeze a narrow, complete build graph into an isolated source snapshot.
    # Adapter writers can continue independently; this UCO entry uses Runtime
    # and SDK directly, and does not compile the unrelated Adapter project.
    $sources=@(& rg --files src/RizToolbox.Runtime src/RizToolbox.Sdk -g '*.cs' -g '*.csproj');if($LASTEXITCODE){throw 'Managed source inventory failed.'}
    $sources+=@('tests/RizToolbox.Runtime.Tests/TransportOwnedAdoptionFixture.cs')
    $paths=@(@($sources|ForEach-Object{Join-Path $repo $_})+@($build.files|ForEach-Object{$_.path})+@(
        $buildPath,$PSCommandPath,$generator,$profileSource,$hostFxrPath,
        (Join-Path $repo 'Directory.Build.props'),(Join-Path $repo 'global.json'),(Join-Path $repo 'dependencies.lock.json'))|Sort-Object -Unique)
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($row in $build.files){if((Hash $row.path) -ne $row.sha256){throw "Candidate artifact changed: $($row.path)"}}
    $manifest.inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    WriteJson $manifest.inputs (Join-Path $output 'input-before.json')
    $snapshot=Join-Path $output 'source-snapshot'
    foreach($row in $manifest.inputs){if($row.path.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        $destination=Join-Path $snapshot ([IO.Path]::GetRelativePath($repo,$row.path));$null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        if((Hash $destination) -ne $row.sha256){throw 'Managed source snapshot did not retain exact frozen input bytes.'}
    }}
    $project=Join-Path $snapshot 'tests/RizToolbox.Runtime.Tests/RizToolbox.Runtime.Tests.csproj'
    [IO.File]::WriteAllText($project,@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Library</OutputType><GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="TransportOwnedAdoptionFixture.cs" /><ProjectReference Include="../../src/RizToolbox.Runtime/RizToolbox.Runtime.csproj" /></ItemGroup>
</Project>
'@,[Text.UTF8Encoding]::new($false))
    $sourceArchive=Join-Path $output 'managed-inputs.zip';$zip=[IO.Compression.ZipFile]::Open($sourceArchive,'Create')
    try{foreach($row in $manifest.inputs){if($row.path.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        $entry=$zip.CreateEntry([IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        $from=[IO.File]::OpenRead($row.path);$to=$entry.Open();try{$from.CopyTo($to)}finally{$from.Dispose();$to.Dispose()}
    }}}finally{$zip.Dispose()}
    $factsPath=Join-Path $output 'OwnedTransportBuildFacts.json'
    & $Python -B $generator --candidate-directory $candidate --output-json $factsPath 2>&1|Out-File -LiteralPath (Join-Path $output 'facts-generation.log')
    if($LASTEXITCODE){throw 'Independent final five-image owned facts generation failed.'}
    $factsHash=Hash $factsPath;$locks.Add([IO.File]::Open($factsPath,'Open','Read','Read'))
    $frozenFacts=Get-Content -LiteralPath $factsPath -Raw|ConvertFrom-Json
    $manifest.facts=[ordered]@{path=$factsPath;sha256=$factsHash}
    $binary=Join-Path $output 'Managed/bin';$buildLog=Join-Path $output 'managed-build.log'
    & $Dotnet build $project -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false "-p:RizEngineProductionProfileSource=$profileSource" -o $binary 2>&1|Out-File -LiteralPath $buildLog
    $manifest.buildExitCode=$LASTEXITCODE;Get-Content -LiteralPath $buildLog -Tail 6
    if($manifest.buildExitCode -ne 0){throw "Managed fixture build failed: $($manifest.buildExitCode)"}
    $assembly=Join-Path $binary 'RizToolbox.Runtime.Tests.dll';$config=Join-Path $binary 'RizToolbox.Runtime.Tests.runtimeconfig.json'
    $managedFiles=@(Get-ChildItem -LiteralPath $binary -File -Recurse|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName}})
    foreach($row in $managedFiles){$locks.Add([IO.File]::Open($row.path,'Open','Read','Read'))}
    $manifest.managedBinaries=$managedFiles
    foreach($mode in @('adopt','duplicate','live-tamper','activity-cost','activity-source-tamper','activity-protection-tamper','activity-export-tamper','activity-iat-tamper','activity-ilt-tamper')){
        $runDirectory=Join-Path $output $mode;$null=New-Item -ItemType Directory -Path $runDirectory
        $nativePath=Join-Path $runDirectory 'native-result.json';$rawPath=Join-Path $runDirectory 'publication.raw'
        $managedPath=Join-Path $runDirectory 'managed-result.json';$argsPath=Join-Path $runDirectory 'managed-args.json'
        WriteJson ([ordered]@{schemaVersion=1;mode=$mode;output=$managedPath;factsPath=$factsPath;factsSha256=$factsHash}) $argsPath
        $argsHash=Hash $argsPath;$argsLock=[IO.File]::Open($argsPath,'Open','Read','Read');$child=[Diagnostics.Process]::new()
        $receipt=[ordered]@{mode=$mode;pid=$null;started=$false;exitCode=$null;timedOut=$false;killed=$false;native=$null;managed=$null;arguments=[ordered]@{path=$argsPath;sha256=$argsHash};passed=$false}
        try{
            $child.StartInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $candidate 'RizTransportOwnedHost.exe'))
            $child.StartInfo.WorkingDirectory=$candidate;$child.StartInfo.UseShellExecute=$false;$child.StartInfo.CreateNoWindow=$true;$child.StartInfo.ErrorDialog=$false
            $child.StartInfo.RedirectStandardOutput=$true;$child.StartInfo.RedirectStandardError=$true
            foreach($argument in @($nativePath,$rawPath,$config,$assembly,$hostFxrPath,$argsPath)){$child.StartInfo.ArgumentList.Add($argument)}
            if(-not $child.Start()){throw 'The owned Native/CLR child did not start.'};$receipt.started=$true;$receipt.pid=$child.Id
            $outTask=$child.StandardOutput.ReadToEndAsync();$errTask=$child.StandardError.ReadToEndAsync()
            $elapsed=[Diagnostics.Stopwatch]::StartNew()
            while(-not $child.WaitForExit(5000)){
                Write-Output "Owned Native/CLR $mode is running: PID=$($receipt.pid), elapsed=$([int]$elapsed.Elapsed.TotalSeconds)s"
                if($elapsed.Elapsed.TotalSeconds -ge $TimeoutSeconds){$receipt.timedOut=$true;$child.Kill();$receipt.killed=$true;$child.WaitForExit();break}
            }
            $receipt.elapsedMilliseconds=$elapsed.ElapsedMilliseconds
            $receipt.exitCode=$child.ExitCode
            [IO.File]::WriteAllText((Join-Path $runDirectory 'stdout.log'),$outTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $runDirectory 'stderr.log'),$errTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
            if(Test-Path -LiteralPath $nativePath){$receipt.native=Get-Content -LiteralPath $nativePath -Raw|ConvertFrom-Json}
            if(Test-Path -LiteralPath $managedPath){$receipt.managed=Get-Content -LiteralPath $managedPath -Raw|ConvertFrom-Json}
            if($receipt.timedOut -or $receipt.exitCode -ne 0 -or -not $receipt.native -or -not $receipt.managed){throw "Owned Native/CLR mode $mode failed (exit $($receipt.exitCode))."}
            if($receipt.native.passed -ne $true -or $receipt.native.pid -ne $receipt.pid -or $receipt.managed.passed -ne $true -or $receipt.managed.pid -ne $receipt.pid -or
                $receipt.native.gameExecuted -ne $false -or $receipt.native.originalGameCodeExecuted -ne $false -or
                $receipt.native.productionAdmissionVerified -ne $false -or (Get-Item -LiteralPath $rawPath).Length -ne 91376){throw 'The actual joint child receipts do not prove the bounded same-process owned run.'}
            $native=$receipt.native;$finalRaw=$rawPath+'.final'
            if($native.kind -ne 'engine-transport-cold-owned-managed' -or $native.actualColdProcess -ne $true -or
                $native.rawPersisted -ne $true -or $native.finalRawPersisted -ne $true -or $native.nativeAbortCalled -ne $false -or
                $native.fixtureFreedClaims -ne $false -or $native.managedAdoptionReturnedSuccess -ne $true -or
                (Get-Item -LiteralPath $finalRaw).Length -ne 91376 -or (Hash $rawPath) -ne (Hash $finalRaw)){
                throw 'The native before/after publication or retained ownership changed across CLR adoption.'
            }
            if(@($native.checkpoints).Count -ne 3){throw 'All three native read/verify/read checkpoints are required.'}
            $checkpointNames=@('before-clr','after-clr-before-entry','after-managed-entry')
            for($i=0;$i -lt 3;++$i){$checkpoint=$native.checkpoints[$i];if($checkpoint.name -ne $checkpointNames[$i] -or
                $checkpoint.attempted -ne $true -or $checkpoint.shape -ne $true -or $checkpoint.immutable -ne $true -or
                $checkpoint.lastErrorPreserved -ne $true -or $checkpoint.version -ne 5 -or $checkpoint.readResult -ne 0 -or
                $checkpoint.verifyResult -ne 0 -or $checkpoint.afterResult -ne 0 -or $checkpoint.clrLoaded -ne ($i -gt 0)){
                    throw 'A real native verification checkpoint or CLR initialization order failed.'
            }}
            $hosting=$native.hosting
            foreach($field in @('attempted','inputsLocked','libraryLoaded','contextCreated','delegateObtained','contextClosed','entryResolved','entryCalled')){
                if($hosting.$field -ne $true){throw "The actual CLR hosting stage did not complete: $field"}
            }
            if($hosting.clrBefore -ne $false -or $hosting.clrAfter -ne $true -or $hosting.managedResult -ne 0 -or $hosting.exceptionCode -ne 0 -or
                $hosting.initializeResult -lt 0 -or $hosting.delegateResult -lt 0 -or $hosting.closeResult -lt 0 -or $hosting.loadAssemblyResult -lt 0){throw 'CLR hosting status does not prove successful same-process execution.'}
            $responsibility=$native.latestNativeResponsibility
            if($responsibility.state -ne 4 -or $responsibility.failure -ne 0 -or $responsibility.runtimeFunctionCount -ne 67 -or
                $responsibility.configuredFamilies -ne 4095 -or $responsibility.boundFamilies -ne 4095 -or $responsibility.retainedFamilies -ne 4095 -or
                $responsibility.committedWindows -ne 68719476735 -or $responsibility.sourceWriteAttempts -ne 36 -or $responsibility.sourceWriteCompletions -ne 36 -or
                $responsibility.protectionOutstanding -ne 0 -or $responsibility.resourceCount -ne 1 -or $responsibility.retainedResources -ne 1 -or
                $responsibility.tableRegistered -ne 1 -or $responsibility.allocationOwned -ne 1 -or $responsibility.rootRetained -ne 1 -or
                $responsibility.lastWin32Error -ne 0 -or $responsibility.epoch -le 0 -or $responsibility.snapshotSequence -le 0 -or
                $responsibility.preparedWindows -ne 68719476735 -or $responsibility.writeAttemptedWindows -ne 68719476735 -or
                $responsibility.outstandingProtectionWindows -ne 0 -or $responsibility.eventOverflow -ne 0){throw 'The actual native responsibility record is incomplete after managed adoption.'}
            $value=$receipt.managed
            $tamperMode=$mode -eq 'live-tamper'
            $activityTamperMode=$mode -in @('activity-source-tamper','activity-protection-tamper','activity-export-tamper','activity-iat-tamper','activity-ilt-tamper')
            if($value.schemaVersion -ne 1 -or $value.kind -ne 'engine-transport-owned-managed-adoption' -or $value.mode -ne $mode -or
                $value.allFamiliesAttached -ne (-not $tamperMode) -or $value.attachedFamilies -ne $(if($tamperMode){0}else{12}) -or $value.coordinatorReused -ne (-not $tamperMode) -or
                $value.countersUnchanged -ne $true -or $value.publicationUnchanged -ne $true -or $value.rootsRetained -ne $true -or
                $value.productionAdmissionGranted -ne $false -or $value.gameExecuted -ne $false -or $value.installed -ne $false -or
                ($mode -eq 'duplicate' -and ($value.duplicateRejected -ne $true -or $value.unknownRetained -ne $true)) -or
                ($mode -in @('adopt','activity-cost') -and ($value.unknownRetained -ne $false -or $value.duplicateRejected -ne $false)) -or
                $value.gameBodiesInvoked -ne $false -or $value.capturedCompletionExercised -ne $false){throw 'The managed receipt lacks complete owned adoption and retention evidence.'}
            if($tamperMode){
                foreach($field in @('liveTamperRejected','liveTamperOwnerVerifyRejected','liveTamperReacquireOwnerRejected',
                    'liveTamperReacquireCoordinatorRejected','unknownRetained','nativeReadyRetained')){
                    if($value.$field -ne $true){throw "Actual live image corruption was not rejected with retained responsibility: $field"}
                }
                foreach($field in @('mutationApplied','alterationObserved','protectionRestoredBeforeInner','flushCompleted','restoreAttempted','restored','restoreVerified')){
                    if($value.liveTamper.$field -ne $true){throw "The live corruption probe lacks actual mutation/restoration evidence: $field"}
                }
                if($value.liveTamper.triggerCount -ne 1 -or $value.liveTamper.failure -or $value.liveTamper.restoreFailure){throw 'The live corruption probe failed its actual native write or restoration.'}
            }
            if($mode -eq 'activity-cost'){
                $cost=$value.activityCost
                if(-not $cost -or $cost.samples -ne 20 -or @($cost.microseconds).Count -ne 20 -or @($cost.allocatedBytes).Count -ne 20 -or
                    $cost.stopwatchFrequency -le 0 -or $cost.allKnownIdle -ne $true -or $cost.currentThreadOnly -ne $true -or
                    $cost.externalSuccessCallbacks -ne $false -or $cost.fullNativeVerifyInsideSamples -ne $false -or $cost.percentileMethod -ne 'nearest-rank'){
                    throw 'Actual activity cost evidence requires twenty known synchronous coordinator reads with raw timing/allocation samples.'
                }
                foreach($sample in @($cost.microseconds)){if(-not [double]::IsFinite([double]$sample) -or $sample -lt 0){throw 'Actual activity timing is invalid.'}}
                foreach($sample in @($cost.allocatedBytes)){if($sample -lt 0){throw 'Actual activity allocation is invalid.'}}
                $sorted=@($cost.microseconds|Sort-Object)
                $allocationSum=($cost.allocatedBytes|Measure-Object -Sum).Sum
                if($cost.p50Microseconds -ne $sorted[9] -or $cost.p95Microseconds -ne $sorted[18] -or $cost.p99Microseconds -ne $sorted[19] -or
                    $cost.totalAllocatedBytes -ne $allocationSum -or $cost.meanAllocatedBytes -ne ($allocationSum/20)){
                    throw 'Activity percentile/allocation summaries differ from the twenty actual raw samples.'
                }
            }
            if($activityTamperMode){
                foreach($field in @('activityTamperRejected','activityDetectionBeforeFullVerify','activityOwnerVerifyRejected','activityReacquireRejected','unknownRetained','nativeReadyRetained')){
                    if($value.$field -ne $true){throw "Next activity did not reject actual corruption with permanent managed refusal and retained native responsibility: $field"}
                }
                $probe=$value.activityTamper
                foreach($field in @('mutationApplied','restoreAttempted','restoreVerified')){
                    if($probe.$field -ne $true){throw "Activity corruption lacks actual mutation/restoration evidence: $field"}
                }
                $expectedProtection=if($mode -in @('activity-export-tamper','activity-iat-tamper','activity-ilt-tamper')){2}else{32}
                if($probe.targetAddress -le 0 -or $probe.originalProtection -ne $expectedProtection -or $probe.restoredProtection -ne $expectedProtection -or
                    $probe.restoredByte -ne $probe.originalByte -or $probe.restoreFailure -or $value.duplicateRejected -ne $false){
                    throw 'Activity corruption did not retain/restore its exact original owned RX source page.'
                }
                if($mode -eq 'activity-source-tamper'){
                    if($probe.kind -ne 'source' -or $probe.observedProtection -ne 32 -or $probe.observedByte -ne ($probe.originalByte -bxor 1) -or $probe.flushCompleted -ne $true){
                        throw 'The next-activity source probe did not expose a real changed/flushed byte with RX protection.'
                    }
                }elseif($mode -eq 'activity-export-tamper'){
                    if($probe.kind -ne 'export' -or $probe.observedProtection -ne 2 -or $probe.observedByte -ne ($probe.originalByte -bxor 1) -or
                        $probe.flushCompleted -ne $true -or $probe.exportFunctionSlotRva -le 0 -or $probe.exportOriginalFunctionRva -le 0){
                        throw 'The next-activity export probe did not expose a real changed/flushed owned Kernel EAT function slot with restored RO protection.'
                    }
                }elseif($mode -in @('activity-iat-tamper','activity-ilt-tamper')){
                    $import=$probe.import;$kind=if($mode -eq 'activity-iat-tamper'){'iat'}else{'ilt'}
                    $edge=@($frozenFacts.edges|Where-Object{$_.index -eq 0})
                    if($edge.Count -ne 1 -or $import.source -ne 3 -or $import.destination -ne 2 -or $import.symbol -ne 'UnityMain2' -or
                        $import.lookupRva -ne $edge[0].lookupRva -or $import.iatRva -ne $edge[0].iatRva -or $import.exportRva -ne $edge[0].exportRva -or
                        $import.sourceModule -le 0 -or $import.destinationModule -le 0 -or $probe.kind -ne $kind -or
                        $import.slotRva -ne $(if($kind -eq 'iat'){$edge[0].iatRva}else{$edge[0].lookupRva}) -or
                        $probe.targetAddress -ne ([uint64]$import.sourceModule+[uint64]$import.slotRva) -or $import.originalValue -le 0 -or
                        $probe.observedImportValue -ne ([uint64]$import.originalValue -bxor [uint64]1) -or $probe.restoredImportValue -ne $import.originalValue -or
                        $probe.observedProtection -ne 2 -or $probe.observedByte -ne ($probe.originalByte -bxor 1) -or $probe.flushCompleted -ne $true){
                        throw 'The actual ILT/IAT mutation/restoration is not bound to the independent parent host import edge.'
                    }
                    if($kind -eq 'iat' -and $import.originalValue -ne ([uint64]$import.destinationModule+[uint64]$edge[0].exportRva)){
                        throw 'The actual IAT original pointer differs from the resolved independent destination export.'
                    }
                }elseif($probe.kind -ne 'protection' -or $probe.observedProtection -ne 2 -or $probe.observedByte -ne $probe.originalByte){
                    throw 'The next-activity protection probe did not expose an unchanged byte on an actual non-executable owned page.'
                }
            }
            if($value.factsSha256 -ne $factsHash -or $value.publicationSha256 -ne (Hash $rawPath) -or @($value.familiesBefore).Count -ne 12 -or
                @($value.familiesAfter).Count -ne 12){throw 'Managed evidence is not bound to the exact parent-frozen facts and actual raw publication.'}
            for($i=0;$i -lt 12;++$i){$before=$value.familiesBefore[$i];$after=$value.familiesAfter[$i]
                if($before.family -ne $i -or $after.family -ne $i -or $before.attached -ne 0 -or $after.attached -ne $(if($tamperMode){0}else{1}) -or
                    $before.unknown -ne 0 -or $after.unknown -ne 0 -or $before.epoch -le 0 -or $after.epoch -ne $before.epoch -or
                    $before.withoutAttachmentSha256 -ne $after.withoutAttachmentSha256 -or
                    (Compare-Object @($before.counters) @($after.counters) -SyncWindow 0)){
                    throw 'One actual native family was reconfigured, used during adoption, or not attached exactly once.'
                }
            }
            if((Hash $argsPath) -ne $argsHash -or (Hash $factsPath) -ne $factsHash){throw 'Independent fixture facts or arguments changed.'}
            $receipt.passed=$true
        }finally{$manifest.children+=,$receipt;$argsLock.Dispose();$child.Dispose()}
    }
    foreach($row in @($manifest.inputs)+@($manifest.managedBinaries)){if((Hash $row.path) -ne $row.sha256){throw "Joint input changed: $($row.path)"}}
    $manifest.inputHashesUnchanged=$true;$manifest.actualNativeClrFullAdoption=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{
    Set-Location -LiteralPath $oldLocation;foreach($lock in $locks){$lock.Dispose()}
    $manifest.artifacts=@(Get-ChildItem -LiteralPath $output -File -Recurse|Where-Object Name -ne 'test-result.json'|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    WriteJson $manifest (Join-Path $output 'test-result.json')
}
Write-Output "Actual same-process owned V5 Native/CLR checks passed: $output"
