#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($OutputDirectory)){throw 'An absolute fresh retirement fixture output is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve prior retirement evidence; choose a fresh output directory.'}
foreach($path in @($repo,$output,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path.'}}
$null=New-Item -ItemType Directory -Path $output
function Hash([string]$File){(Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$File){[IO.File]::WriteAllText($File,($Value|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))}
$relative=@('TweenRetirementContext.h','TweenRetirementContext.cpp','TweenAdmission.h','TweenAdmission.cpp',
    'OperationContext.h','OperationContext.cpp','FirstInstall.h','TweenSourceContext.h','TweenSourceReferences.h','TweenSourceReferences.cpp',
    'TweenSingleEventContext.h','MidContext.h','fixture/TweenRetirementFixture.cpp')
$native=Join-Path $repo 'native/RizToolbox.JudgeContext'
$held=[Collections.Generic.List[IDisposable]]::new();$rows=@();$results=[Collections.Generic.List[object]]::new()
$manifest=[ordered]@{schemaVersion=1;kind='owned-tween-retirement-native-build';passed=$false;sourceHashesUnchanged=$false;
    sources=@();sourceArchive=$null;compileExitCode=$null;fixture=$null;modes=@();gameExecuted=$false;productionHelperExecuted=$false;failure=$null}
try{
    foreach($relativePath in $relative){$file=[IO.Path]::GetFullPath((Join-Path $native $relativePath))
        for($cursor=$file;$cursor;$cursor=Split-Path -Parent $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Fixture inputs cannot cross filesystem links.'}}
        $held.Add([IO.File]::Open($file,'Open','Read','Read'));$rows+=@{path=$file;relativePath='native/RizToolbox.JudgeContext/'+$relativePath;
            sha256=Hash $file;bytes=(Get-Item -LiteralPath $file).Length}}
    $rows+=@{path=$PSCommandPath;relativePath='scripts/Test-TweenRetirementNative.ps1';sha256=Hash $PSCommandPath;
        bytes=(Get-Item -LiteralPath $PSCommandPath).Length};$manifest.sources=$rows
    $archive=Join-Path $output 'source-inputs.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($row in $rows){$entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal)
        $stream=[IO.File]::OpenRead($row.path);$destination=$entry.Open();try{$stream.CopyTo($destination)}finally{$stream.Dispose();$destination.Dispose()}}}finally{$zip.Dispose()}
    $manifest.sourceArchive=@{path=$archive;sha256=Hash $archive}
    $fixture=Join-Path $output 'TweenRetirementFixture.exe'
    $flags='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_TWEEN_ADMISSION_FIXTURE /DRIZ_TWEEN_RETIREMENT_FIXTURE'
    $commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
    foreach($source in @('TweenAdmission','TweenRetirementContext','OperationContext','TweenSourceReferences')){
        $commands+="cl $flags /c /Fo`"$output\$source.obj`" /Fd`"$output\$source.pdb`" `"$native\$source.cpp`""
        $commands+='if errorlevel 1 exit /b 1'
    }
    $commands+="cl $flags /Fo`"$output\fixture.obj`" /Fd`"$output\fixture.pdb`" `"$native\fixture\TweenRetirementFixture.cpp`" `"$output\TweenAdmission.obj`" `"$output\TweenRetirementContext.obj`" `"$output\OperationContext.obj`" `"$output\TweenSourceReferences.obj`" /link /OUT:`"$fixture`" /PDB:`"$output\TweenRetirementFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF"
    $commands+='if errorlevel 1 exit /b 1'
    $cmdFile=Join-Path $output 'build-retirement.cmd';[IO.File]::WriteAllLines($cmdFile,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $cmdFile 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log');$manifest.compileExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw "Native retirement fixture failed to compile: $LASTEXITCODE"}
    $manifest.fixture=@{path=$fixture;sha256=Hash $fixture}
    $executionModes=@('happy','observer-fault','reference-fault','admission-fault','reference-read-fault','unconfigured')
    $executionModes+=@(0..5|ForEach-Object {"observer-enter-$_";"observer-exit-$_"})
    foreach($mode in $executionModes){
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.ArgumentList.Add($mode);$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        try{if(-not $process.Start()){throw 'Could not start the owned native retirement fixture.'}
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            if(-not $process.WaitForExit(60000)){$process.Kill();$null=$process.WaitForExit(10000);throw 'Native retirement fixture timed out.'}
            $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$code=$process.ExitCode
        }finally{$process.Dispose()}
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stdout.log')),$out)
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stderr.log')),$err)
        if($code -ne 0){throw "Native retirement $mode failed: $err $out"}
        $result=$out|ConvertFrom-Json -AsHashtable
        if($result.kind -cne 'owned-tween-retirement-native-fixture' -or $result.passed -ne $true -or $result.failures -ne 0 -or
            $result.actualEntryReservations -ne $true -or $result.actualAdmissionBatch -ne ($mode -cne 'unconfigured') -or
            $result.sourceCountersControlled -ne $true -or $result.gameExecuted -ne $false){throw 'Native retirement fixture returned an invalid execution contract.'}
        if($mode -ceq 'happy' -and ($result.allSixBeforeSnapshotRaces -ne $true -or
            $result.foreignOriginalInFlightDuringCommit -ne $true -or $result.actualReferenceEnd -ne $true -or
            $result.singleInFlightBlocksAcquire -ne $true -or $result.singleCountersControlled -ne $true)){
            throw 'Native retirement did not execute its actual pre-snapshot or overlapping foreign-original proof.'}
        $results.Add($result);Json $result (Join-Path $output ($mode+'-result.json'));Write-Output "PASS native retirement $mode ($($result.checks) checks)."
    }
    foreach($row in $rows){if((Hash $row.path) -cne $row.sha256){throw 'A compiled retirement source changed.'}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{$manifest.modes=@($results);Json $manifest (Join-Path $output 'fixture-manifest.json');foreach($file in $held){$file.Dispose()}}
