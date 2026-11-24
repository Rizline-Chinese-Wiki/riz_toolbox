#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$GameAssemblyPath,
    [Parameter(Mandatory = $true)][string]$DobbyPath,
    [Parameter(Mandatory = $true)][string]$HelperDirectory,
    [string]$SinglePartialProof
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\first-install-mapped-native' }
foreach ($path in @($GameAssemblyPath, $DobbyPath, $HelperDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'GameAssemblyPath, DobbyPath and HelperDirectory must be explicit absolute paths.' }
}
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperDll = Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$gameDll = [IO.Path]::GetFullPath($GameAssemblyPath)
$dobbyDll = [IO.Path]::GetFullPath($DobbyPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\fixture\FirstInstallMappedFixture.cpp'
foreach ($path in @($source, $nativeOutput, $helperDll, $gameDll, $dobbyDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $vcvarsPath, $helperDll, $gameDll, $dobbyDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing file is missing: $path" }
}
$expectedGame = '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'
$expectedDobby = '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'
$beforeGame = (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash.ToLowerInvariant()
$beforeDobby = (Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant()
if ($beforeGame -ne $expectedGame -or $beforeDobby -ne $expectedDobby) {
    throw 'UnsupportedBuild: the mapped publication fixture requires the exact inspected GameAssembly and Dobby images.'
}
$partialPublication=$null
if($SinglePartialProof){
    function PartialHash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
    $partialPath=[IO.Path]::GetFullPath($SinglePartialProof);$partialManifestPath=Join-Path (Split-Path -Parent $partialPath) 'fixture-manifest.json'
    $actualResultBytes=[IO.File]::ReadAllBytes($partialPath);$actualManifestBytes=[IO.File]::ReadAllBytes($partialManifestPath)
    $partial=Get-Content -LiteralPath $partialPath -Raw|ConvertFrom-Json
    $partialManifest=Get-Content -LiteralPath $partialManifestPath -Raw|ConvertFrom-Json
    $nativeBuild=Get-Content -LiteralPath (Join-Path $HelperDirectory 'native-manifest.json') -Raw|ConvertFrom-Json
    if($partial.kind -cne 'first-install-atomic-unknown-snapshot-race' -or $partial.passed -ne $true -or $partial.failures -ne 0 -or
        $partial.actualHelperObjectsLinked -ne $true -or $partial.productionDLLInvoked -ne $false -or
        $partial.singlePartialWriteModes.Count -ne 7 -or $nativeBuild.passed -ne $true -or $nativeBuild.publicationVersion -ne 7 -or
        $partialManifest.fixtureCompileTimeMacro -cne 'RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE'){throw 'A bounded actual partial publication proof is required.'}
    $declaredHelper=@($partialManifest.files|Where-Object{[IO.Path]::GetFileName($_.path) -ceq 'RizToolbox.JudgeContext.dll'})
    if(-not $declaredHelper -or @($declaredHelper|Where-Object sha256 -cne (PartialHash $helperDll)).Count){throw 'The partial fixture used a different helper build.'}
    $objects=@($partialManifest.files|Where-Object{[IO.Path]::GetExtension($_.path) -ceq '.obj'})
    if($objects.Count -ne 21){throw 'The actual partial fixture must link all 21 unchanged helper objects.'}
    foreach($object in $objects){$name=[IO.Path]::GetFileName($object.path);$row=@($nativeBuild.files|Where-Object{[IO.Path]::GetFileName($_.path) -ceq $name})
        if($row.Count -ne 1 -or $row[0].sha256 -cne $object.sha256 -or (PartialHash $object.path) -cne $object.sha256){throw 'A linked partial-fixture helper object differs from the actual native build.'}}
    foreach($row in $partialManifest.files){if((PartialHash $row.path) -cne $row.sha256){throw 'A frozen partial fixture input changed.'}}
    $sourceRows=@($nativeBuild.sources|Where-Object{$_.relativePath.StartsWith('native/',[StringComparison]::Ordinal)})
    foreach($row in $sourceRows){if((PartialHash (Join-Path $repoRoot $row.relativePath)) -cne $row.sha256){throw 'Partial fixture source differs from the final helper archive.'}}
    foreach($stop in 0..6){$row=$partial.singlePartialWriteModes[$stop];if($row.mode -cne "partial-single-$stop" -or $row.passed -ne $true -or
        $row.failures -ne 0 -or $row.stoppedAfterSpan -ne $stop -or $row.phase -ne 4 -or $row.unknown -ne 1 -or
        $row.writesStarted -ne (36+$stop) -or $row.singleAttemptedWrites -ne ($stop+1) -or $row.singleCompletedWrites -ne ($stop+1) -or
        $row.singleConfiguredMask -ne 127 -or $row.singlePublishedMask -ne ((1 -shl ($stop+1))-1) -or
        $row.resourcesRetained -ne $true -or $row.reentryDidNotWrite -ne $true -or $row.actualHelperObjectsLinked -ne $true -or
        $row.gameCodeExecuted -ne $false -or $row.parentEpoch -ne $row.parentRowEpoch -or $row.parentEpoch -le 0){throw 'Actual partial publication mode facts differ.'}}
    $partialPublication=[ordered]@{schemaVersion=1;kind='single-partial-publication-linked-object-evidence';
        manifestRawBase64=[Convert]::ToBase64String($actualManifestBytes);manifestSha256=PartialHash $partialManifestPath;
        resultRawBase64=[Convert]::ToBase64String($actualResultBytes);resultSha256=PartialHash $partialPath;
        sourceFiles=@($sourceRows|Select-Object relativePath,sha256,bytes);actualHelperObjectsLinked=$true;productionDLLInvoked=$false;
        boundedCompileTimeSeam='RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE'}
}
if(Test-Path -LiteralPath $nativeOutput){throw 'Preserve earlier native fixture evidence; use a fresh output directory.'}
New-Item -ItemType Directory -Path $nativeOutput | Out-Null
$fixture = Join-Path $nativeOutput 'FirstInstallMapped.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-first-install-mapped-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\FirstInstallMappedFixture.obj`" /Fd`"$nativeOutput\FirstInstallMappedFixture.compiler.pdb`" `"$source`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\FirstInstallMapped.Fixture.pdb`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Mapped first-install fixture build failed: $LASTEXITCODE" }
$fixtureExit = -1
$modeResults = [Collections.Generic.List[object]]::new()
try {
    $requestedModes=@('normal', 'tamper-source', 'tamper-post', 'tamper-relay', 'tamper-chain', 'tamper-operation', 'tamper-delegate-remove',
        'tamper-single-event','tamper-single-event-unwind','tamper-single-event-chain','tamper-single-event-pdata',
        'tamper-tween-play','tamper-tween-add','tamper-tween-execute','tamper-tween-update','tamper-tween-awake')+
        @(0..21|ForEach-Object{"tamper-tween-source-$_"})+@('tamper-tween-source-relay','tamper-tween-source-unwind','tamper-tween-source-table')
    $singleWidths=@(9,6,9,5,9,6,9)
    foreach($span in 0..6){foreach($offset in 0..($singleWidths[$span]-1)){$requestedModes+="tamper-single-span-$span-byte-$offset"}}
    $singleBlockOffsets=@(
        foreach($site in 0..2){foreach($offset in @(0,4,5,7,9,14,15,20)){$site*256+$offset}}
        foreach($site in 0..2){foreach($offset in @(0,3,23,24,35)){$site*256+64+$offset}}
        foreach($site in 0..2){foreach($offset in @(0,11)){0x380+$site*12+$offset}}
        foreach($relay in @(0x500,0x520,0x540)){foreach($offset in @(0,5,6,13)){$relay+$offset}}
        foreach($site in 0..2){foreach($offset in @(0x15,0x3f,0x64,0xff)){$site*256+$offset}}
        0x300;0x37f;0x3a4;0x4ff;0x50e;0x51f;0x52e;0x53f;0x54e;0x600;0xfff
    )|Sort-Object -Unique
    foreach($offset in $singleBlockOffsets){$requestedModes+="tamper-single-block-byte-$offset"}
    foreach($span in 0..6){$requestedModes+="tamper-single-source-protection-$span"}
    $requestedModes+='tamper-single-block-protection'
    foreach ($mode in $requestedModes) {
        $start = [Diagnostics.ProcessStartInfo]::new($fixture)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($helperDll, $dobbyDll, $gameDll)) { $start.ArgumentList.Add($argument) }
        if ($mode -ne 'normal') { $start.ArgumentList.Add($mode) }
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start the owned mapped publication $mode child." }
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) {
            # Only the specific fixture process created here is terminated.
            $child.Kill($true)
            $child.WaitForExit()
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $nativeOutput "$mode.stdout.log") -Encoding utf8
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $nativeOutput "$mode.stderr.log") -Encoding utf8
            $child.Dispose()
            throw "The owned mapped publication $mode child exceeded its finite timeout."
        }
        $fixtureExit = $child.ExitCode
        $outText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        $child.Dispose()
        $outText | Set-Content -LiteralPath (Join-Path $nativeOutput "$mode.stdout.log") -Encoding utf8
        $errorText | Set-Content -LiteralPath (Join-Path $nativeOutput "$mode.stderr.log") -Encoding utf8
        $jsonLines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
        if ($fixtureExit -ne 0 -or $jsonLines.Count -ne 1) {
            throw "Mapped publication $mode failed (exit $fixtureExit): $errorText $outText"
        }
        $modeResult = $jsonLines[0] | ConvertFrom-Json -AsHashtable
        $single=$modeResult.singleEventPublication
        if($single.capabilityVersion -ne 1 -or $single.byteSize -ne 416 -or $single.phase -ne 3 -or $single.unknown -ne 0 -or $single.reserved -ne 0 -or
            $single.operationId -ne 25 -or $single.kind -ne 3 -or $single.slot -ne 31 -or
            $single.expectedWrites -ne 7 -or $single.epoch -le 0 -or $single.parentEpoch -ne $single.parentRowEpoch -or $single.parentEpochMatchesOperation25 -ne $true -or
            $single.configuredMask -ne 127 -or $single.publishedMask -ne 127 -or $single.attemptedWrites -ne 7 -or $single.completedWrites -ne 7 -or
            $single.allocationSize -ne 4096 -or $single.registeredFunctionCount -ne 3 -or $single.lookupChecks -ne 15 -or $single.boundaryChecks -ne 15 -or
            $modeResult.singleSourceBytesVerified -ne $true -or $modeResult.singleBlockBytesVerified -ne 4096 -or $modeResult.singleUnwindVerified -ne $true -or
            $modeResult.singleCountersZero -ne $true -or $modeResult.singleSyntheticBoundaryChecks -ne 15){throw 'The actual Single publication evidence is incomplete.'}
        if(($single.spanRvas -join ',') -cne '12324999,12325127,12325133,12325213,12325218,12325403,12325409' -or
            ($single.spanWidths -join ',') -cne '9,6,9,5,9,6,9' -or ($single.stubSizes -join ',') -cne '21,21,21' -or ($single.unwindSizes -join ',') -cne '36,36,36'){
            throw 'The actual seven Single span declarations differ.'}
        $span=-1;$byte=-1;$block=-1;$protection=-1
        if($mode -cmatch '^tamper-single-span-(\d+)-byte-(\d+)$'){$span=[int]$Matches[1];$byte=[int]$Matches[2]}
        elseif($mode -cmatch '^tamper-single-block-byte-(\d+)$'){$block=[int]$Matches[1]}
        elseif($mode -cmatch '^tamper-single-source-protection-(\d+)$'){$protection=[int]$Matches[1]}
        elseif($mode -ceq 'tamper-single-block-protection'){$protection=7}
        if($modeResult.tamperedSingleSpan -ne $span -or $modeResult.tamperedSingleOffset -ne $byte -or $modeResult.tamperedSingleBlockOffset -ne $block -or
            $modeResult.tamperedSingleProtection -ne $protection){throw 'A Single mutation did not execute its declared exact address/protection.'}
        $singleTamper=$span -ge 0 -or $block -ge 0 -or $protection -ge 0
        if($modeResult.fixtureProtectionChanges -ne $(if($protection -ge 0){1}else{0}) -or
            $modeResult.fixtureRestorations -ne $(if($singleTamper){1}else{0}) -or $modeResult.singlePermanentUnknownAfterRestore -ne $singleTamper){
            throw 'The actual protection/restore/permanent Unknown evidence differs.'}
        $tweenIndex=@('tamper-tween-play','tamper-tween-add','tamper-tween-execute','tamper-tween-update','tamper-tween-awake').IndexOf($mode)
        if($tweenIndex -ge 0){
            $expectedTail=@(3575,267,5907,5171,321)[$tweenIndex]
            if($modeResult.tamperedOperationId -ne (20+$tweenIndex) -or $modeResult.tamperedOperationBodyOffset -ne $expectedTail -or
                $modeResult.tweenFullBodyTailTamperVerified -ne $true){throw 'The actual Tween full-body tail mutation was not verified.'}
        }elseif($mode -ceq 'tamper-single-event'){
            if($modeResult.tamperedOperationId -ne 25 -or $modeResult.tamperedOperationBodyOffset -ne 646 -or
                $modeResult.tweenFullBodyTailTamperVerified -ne $false -or
                $modeResult.singleEventFullBodyTailTamperVerified -ne $true){throw 'The actual UnityEvent<float> full-body tail mutation was not verified.'}
        }elseif($modeResult.tamperedOperationId -ne -1 -or $modeResult.tamperedOperationBodyOffset -ne 0 -or
            $modeResult.tweenFullBodyTailTamperVerified -ne $false){throw 'A non-Tween case claimed a Tween body mutation.'}
        $singleMetadataIndex=@('tamper-single-event-unwind','tamper-single-event-chain','tamper-single-event-pdata').IndexOf($mode)
        $expectedContinuation=if($mode -ceq 'tamper-single-event'){2}elseif($singleMetadataIndex -ge 0){1}else{-1}
        if($modeResult.singleEventContinuationsVerified -ne 3 -or
            $modeResult.tamperedSingleContinuation -ne $expectedContinuation -or
            $modeResult.tamperedSingleMetadata -ne ($singleMetadataIndex+1)){
            throw 'The Single CHAININFO body/unwind/parent/pdata mutation identity differs.'
        }
        if ($modeResult.passed -ne $true -or $modeResult.failures -ne 0 -or $modeResult.checks -le 0 -or
            $modeResult.writesStarted -ne 64 -or $modeResult.gameCodeExecuted -ne $false -or
            $modeResult.gameRuntimeVerified -ne $false -or $modeResult.mappedImageOnly -ne $true -or
            $modeResult.mode -ne $mode -or $modeResult.v2ReadyVerified -ne $true -or $modeResult.v2UnknownVerified -ne $true -or
            $modeResult.operationReceiptVersion -ne 7 -or $modeResult.operationRowsVerified -ne 26 -or
            $modeResult.invalidV7HeaderCases -ne 17 -or $modeResult.v7UnknownVerified -ne $true -or $modeResult.operationCallbacksExecuted -ne 0 -or
            $modeResult.tweenSourceRowsVerified -ne 22 -or $modeResult.tweenSourceBindingsVerified -ne 43 -or
            $modeResult.tweenSourceFunctionsVerified -ne 56 -or $modeResult.tweenSourceCallbacksExecuted -ne 0 -or
            $modeResult.invalidV2HeaderCases -ne 9 -or $modeResult.v2CallerIdentityCanonicalized -ne $true -or
            $modeResult.listenerSourceBytesVerified -ne $true -or $modeResult.listenerPostBytesVerified -ne $true -or
            $modeResult.listenerRxVerified -ne $true -or $modeResult.listenerUnwindVerified -ne $true -or
            $modeResult.listenerBoundaryChecks -ne 12 -or $modeResult.listenerLookupChecks -ne 12 -or
            $modeResult.listenerRestoredProtectionChecks -ne 6 -or $modeResult.listenerEpoch -le 0 -or
            $modeResult.noAdditionalProducerWrite -ne $true -or $modeResult.sourceReturnPcObservedByExecution -ne $false -or
            $modeResult.currentPhase -ne 4 -or $modeResult.currentUnknown -ne 1 -or $modeResult.currentWritesStarted -ne 64 -or
            ($mode -ne 'normal' -and ($modeResult.tamperDetected -ne $true -or $modeResult.fixtureTamperWrites -ne $(if($protection -ge 0){0}else{1}))) -or
            ($mode -eq 'normal' -and ($modeResult.tamperDetected -ne $false -or $modeResult.fixtureTamperWrites -ne 0))) {
            throw "Mapped publication $mode result is incomplete or inconsistent."
        }
        $jsonLines[0] | Set-Content -LiteralPath (Join-Path $nativeOutput "$mode-result.json") -Encoding utf8
        $modeResults.Add($modeResult)
        Write-Output "PASS mapped publication $mode ($($modeResult.checks) checks); no game code executed."
    }
    $normalResult = $modeResults[0]
    $normalResult.independentProcesses = $modeResults.Count
    $normalResult.allProcessChecks = ($modeResults | Measure-Object -Property checks -Sum).Sum
    $normalResult.tamperModes = @($modeResults | Select-Object -Skip 1)
    $normalResult.singleBlockTamperOffsets=@($singleBlockOffsets)
    $normalResult.singleSourceByteTamperCount=53
    $normalResult.singleBlockSealBytes=4096
    $normalResult.singleBlockTamperIsExhaustive=$false
    if($partialPublication){$normalResult.singlePartialPublication=$partialPublication}
    $normalResult | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
}
finally {
    $afterGame = (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $afterDobby = (Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{ schemaVersion = 1; kind = 'mapped-fixture-source-integrity'; gameAssemblyPath = $gameDll;
        gameBeforeSha256 = $beforeGame; gameAfterSha256 = $afterGame; dobbyBeforeSha256 = $beforeDobby; dobbyAfterSha256 = $afterDobby;
        unchanged = ($afterGame -eq $beforeGame -and $afterDobby -eq $beforeDobby); fixtureExitCode = $fixtureExit } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $nativeOutput 'source-image-check.json') -Encoding utf8
    if ($afterGame -ne $beforeGame -or $afterDobby -ne $beforeDobby) { throw 'Source image hash changed during the isolated mapped-image fixture.' }
}
if ($fixtureExit -ne 0) { throw "Mapped first-install publication fixture failed: $fixtureExit" }
$result = Get-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Raw | ConvertFrom-Json
if ($result.kind -ne 'fixed-mapped-image-first-install-publication' -or $result.passed -ne $true -or $result.failures -ne 0 -or
    $result.phase -ne 3 -or $result.unknown -ne 0 -or $result.writesStarted -ne 64 -or
    $result.phaseObservation -ne 'before-ready-reentry' -or $result.readyReentryStatus -ne 2 -or
    $result.readyReentryRejected -ne $true -or $result.reentryBytesUnchanged -ne $true -or
    $result.currentPhase -ne 4 -or $result.currentUnknown -ne 1 -or $result.currentWritesStarted -ne 64 -or
    $result.independentProcesses -ne $requestedModes.Count -or $result.tamperModes.Count -ne ($requestedModes.Count-1) -or
    $result.mappedImageOnly -ne $true -or $result.gameCodeExecuted -ne $false -or $result.gameRuntimeVerified -ne $false) {
    throw 'Mapped first-install publication result is incomplete or inconsistent.'
}
$files = @($helperDll, $dobbyDll, $gameDll, $fixture) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion = 1; kind = 'fixed-mapped-image-first-install-publication'; mappedImageOnly = $true;
    mappingMode = 'LoadLibraryExW DONT_RESOLVE_DLL_REFERENCES'; gameCodeExecuted = $false; originalInitInvoked = $false;
    sourceImagesUnchanged = $true; firstInstallSafetyVerified = $false; gameRuntimeVerified = $false; files = @($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Mapped first-install publication fixture: $nativeOutput"
