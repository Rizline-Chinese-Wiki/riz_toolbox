#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($OutputDirectory)){throw 'An absolute fresh owned Single fixture output is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve earlier Single machine evidence; choose a fresh output.'}
foreach($path in @($repo,$output,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path.'}}
$null=New-Item -ItemType Directory -Path $output
function Hash([string]$File){(Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$File){[IO.File]::WriteAllText($File,($Value|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))}
$files=@('native/RizToolbox.JudgeContext/TweenSingleEventContext.h','native/RizToolbox.JudgeContext/TweenSingleEventContext.cpp',
    'native/RizToolbox.JudgeContext/TweenSingleEventEntry.asm','native/RizToolbox.JudgeContext/MidContext.h',
    'native/RizToolbox.JudgeContext/OperationContext.cpp','native/RizToolbox.JudgeContext/OperationContext.h',
    'native/RizToolbox.JudgeContext/TweenAdmission.cpp','native/RizToolbox.JudgeContext/TweenAdmission.h',
    'native/RizToolbox.JudgeContext/TweenRetirementContext.cpp','native/RizToolbox.JudgeContext/TweenRetirementContext.h',
    'native/RizToolbox.JudgeContext/FirstInstall.h',
    'native/RizToolbox.JudgeContext/fixture/TweenSingleEventFixture.cpp','native/RizToolbox.JudgeContext/fixture/TweenSingleEventFixtureCaller.asm',
    'scripts/Test-TweenSingleEventNative.ps1')
$locks=[Collections.Generic.List[IDisposable]]::new();$rows=@();$results=[Collections.Generic.List[object]]::new()
$manifest=[ordered]@{schemaVersion=2;kind='owned-tween-single-event-machine-build';passed=$false;compileExitCode=$null;
    sourceHashesUnchanged=$false;sources=@();sourceArchive=$null;fixture=$null;modes=@();failure=$null;
    toolchain=$null;buildCommand=$null;buildLog=$null;plannedModes=@();
    controlledPublication=$true;productionHelperExecuted=$false;officialMetadataVerified=$false;gameExecuted=$false}
try{
    foreach($name in $files){$path=[IO.Path]::GetFullPath((Join-Path $repo $name));
        for($cursor=$path;$cursor;$cursor=Split-Path -Parent $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned Single sources cannot cross filesystem links.'}}
        $locks.Add([IO.File]::Open($path,'Open','Read','Read'));$rows+=@{path=$path;relativePath=$name;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length}}
    $manifest.sources=$rows;$archive=Join-Path $output 'single-machine-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($row in $rows){$entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal);
        $source=[IO.File]::OpenRead($row.path);$destination=$entry.Open();try{$source.CopyTo($destination)}finally{$source.Dispose();$destination.Dispose()}}}finally{$zip.Dispose()}
    $manifest.sourceArchive=@{path=$archive;sha256=Hash $archive;bytes=(Get-Item -LiteralPath $archive).Length}
    $native=Join-Path $repo 'native/RizToolbox.JudgeContext';$fixture=Join-Path $output 'TweenSingleEventFixture.exe'
    $flags='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_TWEEN_ADMISSION_FIXTURE /DRIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY /DRIZ_TWEEN_SINGLE_EVENT_FIXTURE'
    $manifest.toolchain=@{vcvars=$VcVars;cxxFlags=$flags;assemblyFlags='/nologo /c /Zi';postStubBytes=21;postStubBoundaries=@(0,5,7,9,15);helperBoundariesPerEntry=10}
    $commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
    foreach($entry in @(@('TweenSingleEventEntry.asm','entry.obj'),@('fixture/TweenSingleEventFixtureCaller.asm','caller.obj'))){
        $commands+="ml64 /nologo /c /Zi /Fo`"$output\$($entry[1])`" `"$native\$($entry[0])`"";$commands+='if errorlevel 1 exit /b 1'}
    foreach($source in @('OperationContext','TweenAdmission','TweenRetirementContext','TweenSingleEventContext')){
        $commands+="cl $flags /c /Fo`"$output\$source.obj`" /Fd`"$output\$source.pdb`" `"$native\$source.cpp`"";$commands+='if errorlevel 1 exit /b 1'}
    $commands+="cl $flags /Fo`"$output\fixture.obj`" /Fd`"$output\fixture.pdb`" `"$native\fixture\TweenSingleEventFixture.cpp`" `"$output\OperationContext.obj`" `"$output\TweenAdmission.obj`" `"$output\TweenRetirementContext.obj`" `"$output\TweenSingleEventContext.obj`" `"$output\entry.obj`" `"$output\caller.obj`" /link /OUT:`"$fixture`" /PDB:`"$output\TweenSingleEventFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF"
    $commands+='if errorlevel 1 exit /b 1';$cmd=Join-Path $output 'build-single-machine.cmd';[IO.File]::WriteAllLines($cmd,$commands,[Text.UTF8Encoding]::new($false))
    $manifest.buildCommand=@{path=$cmd;sha256=Hash $cmd;bytes=(Get-Item -LiteralPath $cmd).Length}
    & $env:ComSpec /d /c $cmd 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log');$manifest.compileExitCode=$LASTEXITCODE
    $manifest.buildLog=@{path=(Join-Path $output 'build.log');sha256=Hash (Join-Path $output 'build.log');bytes=(Get-Item -LiteralPath (Join-Path $output 'build.log')).Length}
    if($LASTEXITCODE -ne 0){throw "Strict owned Single machine build failed: $LASTEXITCODE"}
    $manifest.fixture=@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    $modes=@('happy','machine-state','late-attach','nested','nested-same','foreign','worker-foreign','worker-owned',
        'observer-cpp','observer-seh','observer-nonzero','mid-cpp','mid-seh','mid-nonzero','returned-cpp','returned-seh','returned-nonzero',
        'original-cpp','original-cpp-1','original-cpp-2','original-seh','original-seh-1','original-seh-2',
        'before-call-cpp','before-call-seh','target-seh','target-seh-2','null-list','abnormal-cpp','abnormal-seh','abnormal-nonzero',
        'missing-post-0','missing-post-1','missing-post-2','parent-overflow','child-overflow','no-mid','duplicate-mid','worker-read','stale-read','alias-read','copied-read')
    $manifest.plannedModes=$modes
    foreach($mode in $modes){
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.ArgumentList.Add($mode);$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        try{if(-not $process.Start()){throw 'Could not start the owned Single machine fixture.'}
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            if(-not $process.WaitForExit(60000)){$process.Kill();$null=$process.WaitForExit(10000);throw 'Owned Single fixture timed out.'}
            $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$code=$process.ExitCode
        }finally{$process.Dispose()}
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stdout.log')),$out);[IO.File]::WriteAllText((Join-Path $output ($mode+'.stderr.log')),$err)
        $result=$null;try{$result=$out|ConvertFrom-Json -AsHashtable}catch{}
        $record=[ordered]@{mode=$mode;exitCode=$code;result=$result;
            stdout=@{path=(Join-Path $output ($mode+'.stdout.log'));sha256=Hash (Join-Path $output ($mode+'.stdout.log'));bytes=(Get-Item -LiteralPath (Join-Path $output ($mode+'.stdout.log'))).Length};
            stderr=@{path=(Join-Path $output ($mode+'.stderr.log'));sha256=Hash (Join-Path $output ($mode+'.stderr.log'));bytes=(Get-Item -LiteralPath (Join-Path $output ($mode+'.stderr.log'))).Length}}
        $results.Add($record)
        if($code -ne 0){throw "Owned Single $mode failed exit $code : $err $out"}
        if($result.kind -cne 'owned-tween-single-event-machine-fixture' -or $result.mode -cne $mode -or $result.passed -ne $true -or
            $result.failures -ne 0 -or $result.checks -le 0 -or $result.actualSingleEntry -ne $true -or $result.actualMachineMidAndCalls -ne $true -or
            $result.privateNativeParentBinding -ne $true -or $result.controlledPublication -ne $true -or $result.officialMetadataVerified -ne $false -or
            $result.productionHelperExecuted -ne $false -or $result.gameExecuted -ne $false -or $result.postStubBytes -ne 21 -or
            $result.postStubConditionalJumps -ne 2 -or $result.staticOwnedContinuationRF -ne $true -or $result.stubSyntheticBoundaryUnwinds -ne 15 -or
            $result.machineStateComparisons -ne $(if($mode -ceq 'machine-state'){9}else{0})){throw 'Owned Single fixture execution contract differs.'}
        Json $result (Join-Path $output ($mode+'-result.json'));Write-Output "PASS owned Single machine $mode ($($result.checks) checks)."
    }
    foreach($row in $rows){if((Hash $row.path) -cne $row.sha256){throw 'A compiled owned Single input changed.'}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{
    $manifest.modes=@($results);Json $manifest (Join-Path $output 'fixture-manifest.json');foreach($file in $locks){$file.Dispose()}
    $artifacts=@(Get-ChildItem -LiteralPath $output -File | ForEach-Object {@{name=$_.Name;bytes=$_.Length;sha256=Hash $_.FullName}})
    Json @{schemaVersion=1;kind='owned-tween-single-event-artifacts';artifacts=$artifacts} (Join-Path $output 'artifact-hashes.json')
}
