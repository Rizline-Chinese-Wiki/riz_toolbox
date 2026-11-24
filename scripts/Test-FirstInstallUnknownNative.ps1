#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$GameAssemblyPath,
    [Parameter(Mandatory = $true)][string]$DobbyPath,
    [Parameter(Mandatory = $true)][string]$HelperDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\first-install-unknown-native' }
foreach ($path in @($GameAssemblyPath, $DobbyPath, $HelperDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'GameAssemblyPath, DobbyPath and HelperDirectory must be explicit absolute paths.' }
}
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperRoot = [IO.Path]::GetFullPath($HelperDirectory)
$helperDll = Join-Path $helperRoot 'RizToolbox.JudgeContext.dll'
$helperLib = Join-Path $helperRoot 'RizToolbox.JudgeContext.lib'
$gameDll = [IO.Path]::GetFullPath($GameAssemblyPath)
$dobbyDll = [IO.Path]::GetFullPath($DobbyPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$producerSource = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\FirstInstall.cpp'
$fixtureSource = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\fixture\FirstInstallUnknownFixture.cpp'
foreach ($path in @($producerSource, $fixtureSource, $nativeOutput, $helperDll, $helperLib, $gameDll, $dobbyDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($producerSource, $fixtureSource, $vcvarsPath, $helperDll, $helperLib, $gameDll, $dobbyDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing file is missing: $path" }
}
$beforeGame = (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash.ToLowerInvariant()
$beforeDobby = (Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant()
if ($beforeGame -ne '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79' -or
    $beforeDobby -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the unknown snapshot fixture requires the exact inspected GameAssembly and Dobby images.'
}
if(Test-Path -LiteralPath $nativeOutput){throw 'Preserve earlier native fixture evidence; use a fresh output directory.'}
New-Item -ItemType Directory -Path $nativeOutput | Out-Null
$fixtureHelper = Join-Path $nativeOutput 'RizToolbox.JudgeContext.dll'
if (-not [string]::Equals($fixtureHelper, $helperDll, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $helperDll -Destination $fixtureHelper -Force
}
$helperHash = (Get-FileHash -LiteralPath $helperDll -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-FileHash -LiteralPath $fixtureHelper -Algorithm SHA256).Hash.ToLowerInvariant() -ne $helperHash) {
    throw 'The isolated EXE dependency copy differs from the actual helper.'
}
$fixture = Join-Path $nativeOutput 'FirstInstallUnknown.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-first-install-unknown-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_FIRST_INSTALL_UNKNOWN_FIXTURE /Fo`"$nativeOutput\FirstInstallUnderTest.obj`" /Fd`"$nativeOutput\FirstInstallUnderTest.compiler.pdb`" `"$producerSource`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$helperDll`" > `"$nativeOutput\production-helper-exports.txt`"", 'if errorlevel 1 exit /b 1'
)
$actualObjects=@('JudgeContext','JudgeEntry','MidEntry','ListenerEntry','ListenerContext','InitializationContext','CoroutineContext','OperationContext',
    'TweenAdmission','TweenRetirementContext','TweenSourceContext','TweenSourceReferences','TweenSourcePublication','TweenSourceEntry',
    'TweenSingleEventContext','TweenSingleEventEntry','EngineNodeContext','VoidContext','MidContext','BootstrapVoidContext','RuntimeInvokeContext')
$objectArguments=@($actualObjects|ForEach-Object{$path=Join-Path $helperRoot ($_.ToString()+'.obj');if(-not(Test-Path -LiteralPath $path)){throw 'A real helper build object is missing.'};'"'+$path+'"'}) -join ' '
$commands+="cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\FirstInstallUnknownFixture.obj`" /Fd`"$nativeOutput\FirstInstallUnknownFixture.compiler.pdb`" `"$fixtureSource`" `"$nativeOutput\FirstInstallUnderTest.obj`" $objectArguments bcrypt.lib /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\FirstInstallUnknown.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF"
$commands+='if errorlevel 1 exit /b 1'
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "First-install unknown fixture build failed: $LASTEXITCODE" }
if ((Get-Content -LiteralPath (Join-Path $nativeOutput 'production-helper-exports.txt') -Raw).Contains('RizFirstInstallFixture_')) {
    throw 'The production helper unexpectedly exports a fixture seam.'
}
$fixtureExit = -1
try {
    $start = [Diagnostics.ProcessStartInfo]::new($fixture)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($dobbyDll, $gameDll)) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    if (-not $child.Start()) { throw 'Could not start the owned unknown snapshot fixture.' }
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(60000)) {
        # Only this owned test process is terminated, never a game process.
        $child.Kill($true)
        $child.WaitForExit()
        $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture.stdout.log') -Encoding utf8
        $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture.stderr.log') -Encoding utf8
        $child.Dispose()
        throw 'The owned unknown snapshot fixture exceeded its finite timeout.'
    }
    $fixtureExit = $child.ExitCode
    $outText = $stdout.GetAwaiter().GetResult()
    $errorText = $stderr.GetAwaiter().GetResult()
    $child.Dispose()
    $outText | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture.stdout.log') -Encoding utf8
    $errorText | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture.stderr.log') -Encoding utf8
    $jsonLines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
    if ($fixtureExit -ne 0 -or $jsonLines.Count -ne 1) {
        throw "Unknown snapshot fixture failed (exit $fixtureExit): $errorText $outText"
    }
    $jsonLines[0] | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
}
finally {
    $afterGame = (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $afterDobby = (Get-FileHash -LiteralPath $dobbyDll -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{ schemaVersion = 1; kind = 'unknown-fixture-source-integrity'; gameAssemblyPath = $gameDll;
        gameBeforeSha256 = $beforeGame; gameAfterSha256 = $afterGame; dobbyBeforeSha256 = $beforeDobby; dobbyAfterSha256 = $afterDobby;
        unchanged = ($afterGame -eq $beforeGame -and $afterDobby -eq $beforeDobby); fixtureExitCode = $fixtureExit } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $nativeOutput 'source-image-check.json') -Encoding utf8
    if ($afterGame -ne $beforeGame -or $afterDobby -ne $beforeDobby) { throw 'Source image hash changed during the isolated unknown-snapshot fixture.' }
}
if ($fixtureExit -ne 0) { throw "First-install unknown snapshot fixture failed: $fixtureExit" }
$result = Get-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Raw | ConvertFrom-Json
if ($result.kind -ne 'first-install-atomic-unknown-snapshot-race' -or $result.passed -ne $true -or $result.failures -ne 0 -or
    $result.readySnapshotPhase -ne 3 -or $result.readySnapshotUnknown -ne 0 -or $result.writesStarted -ne 64 -or
    $result.unknownAtomicPrecedesEveryRead -ne $true -or $result.detailWriterPausedDuringReads -ne $true -or
    $result.readsWhileWriterPaused -ne 100 -or $result.observedUnknownSnapshots -ne 100 -or $result.reentryStatus -ne 2 -or
    $result.currentPhase -ne 4 -or $result.currentUnknown -ne 1 -or $result.currentWritesStarted -ne 64 -or
    $result.v7UnknownVerified -ne $true -or $result.v7ObservedUnknownSnapshots -ne 100 -or
    $result.v2ReadyVerified -ne $true -or $result.v2UnknownVerified -ne $true -or
    $result.v2ObservedUnknownSnapshots -ne 100 -or $result.v2ReadsWhileWriterPaused -ne 100 -or
    $result.invalidV2HeaderCases -ne 9 -or $result.v2CallerIdentityCanonicalized -ne $true -or
    $result.listenerSourceBytesVerified -ne $true -or $result.listenerPostBytesVerified -ne $true -or
    $result.listenerRxVerified -ne $true -or $result.listenerUnwindVerified -ne $true -or
    $result.listenerBoundaryChecks -ne 12 -or $result.listenerLookupChecks -ne 12 -or
    $result.listenerRestoredProtectionChecks -ne 6 -or $result.listenerBytesUnchanged -ne $true -or
    $result.sourceReturnPcObservedByExecution -ne $false -or $result.noAdditionalProducerWrite -ne $true -or
    $result.gameCodeExecuted -ne $false -or $result.transportInvocations -ne 0) {
    throw 'First-install unknown snapshot result is incomplete or inconsistent.'
}
$partialModes=@()
foreach($stop in 0..6){
    $mode="partial-single-$stop";$start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @($dobbyDll,$gameDll,$mode)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try{if(-not $process.Start()){throw 'Could not start partial Single fixture.'}
        $outTask=$process.StandardOutput.ReadToEndAsync();$errTask=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Partial Single fixture exceeded finite timeout.'}
        $out=$outTask.GetAwaiter().GetResult();$err=$errTask.GetAwaiter().GetResult();$code=$process.ExitCode
    }finally{$process.Dispose()}
    $out|Set-Content -LiteralPath (Join-Path $nativeOutput ($mode+'.stdout.log')) -Encoding utf8
    $err|Set-Content -LiteralPath (Join-Path $nativeOutput ($mode+'.stderr.log')) -Encoding utf8
    if($code){throw "Partial Single $mode failed: $err $out"}
    $value=$out|ConvertFrom-Json
    if($value.kind -cne 'first-install-single-partial-publication' -or $value.mode -cne $mode -or $value.passed -ne $true -or
        $value.failures -ne 0 -or $value.checks -le 0 -or $value.stoppedAfterSpan -ne $stop -or $value.phase -ne 4 -or $value.unknown -ne 1 -or
        $value.writesStarted -ne (36+$stop) -or $value.singleAttemptedWrites -ne (1+$stop) -or $value.singleCompletedWrites -ne (1+$stop) -or
        $value.singlePublishedMask -ne ((1 -shl ($stop+1))-1) -or $value.singleConfiguredMask -ne 127 -or
        $value.singleEpoch -le 0 -or $value.parentEpoch -le 0 -or $value.parentEpoch -ne $value.parentRowEpoch -or
        $value.resourcesRetained -ne $true -or $value.reentryDidNotWrite -ne $true -or $value.actualHelperObjectsLinked -ne $true -or
        $value.gameCodeExecuted -ne $false -or $value.transportInvocations -ne 0){throw 'Actual partial Single publication facts differ.'}
    $partialModes+=$value;Write-Output "PASS partial Single publication $stop ($($value.checks) checks)."
}
$result|Add-Member -NotePropertyName singlePartialWriteModes -NotePropertyValue $partialModes
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
$files = @($producerSource, $fixtureSource, $helperDll, $helperLib, $fixtureHelper, $dobbyDll, $gameDll, $fixture) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$files+=@($actualObjects|ForEach-Object{$path=Join-Path $helperRoot ($_.ToString()+'.obj');@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}})
[ordered]@{ schemaVersion = 1; kind = 'first-install-atomic-unknown-snapshot-race'; fixtureCompileTimeMacro = 'RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE';
    productionHelperHasFixtureExport = $false; actualProducerSourceCompiledIntoIsolatedExe = $true; realHelperTransports = $true;
    mappedImageOnly = $true; gameCodeExecuted = $false; originalInitInvoked = $false; sourceImagesUnchanged = $true;
    firstInstallSafetyVerified = $false; gameRuntimeVerified = $false; files = @($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "First-install unknown snapshot fixture: $nativeOutput"
