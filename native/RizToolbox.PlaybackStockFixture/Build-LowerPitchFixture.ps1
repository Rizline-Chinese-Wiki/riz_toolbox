#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
 [string]$VcVars='C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat')
$ErrorActionPreference='Stop'
$continuation=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=$continuation+[IO.Path]::DirectorySeparatorChar
if(-not $output.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh directory in this continuation.'}
if(($output+$VcVars+$PSScriptRoot) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path characters.'}
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=(Get-FileHash -LiteralPath $p.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$p.Length}}
$null=New-Item -ItemType Directory -Path $output
$frozen=Join-Path $output 'frozen-source'
$null=New-Item -ItemType Directory -Path $frozen
$flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf /DRIZ_STOCK_PROGRESS_RESOURCE_PROFILE /DRIZ_STOCK_UNITY_BACKING_PROFILE /DRIZ_STOCK_LOWER_PITCH_PROFILE'
$units=@(
 @{source='PlaybackStockFixture.cpp';object='PlaybackStockFixture.obj';tool='cl';flags=$flags},
 @{source='UnityBackingFixtureStorage.cpp';object='UnityBackingFixtureStorage.obj';tool='cl';flags=$flags},
 @{source='LowerPitchWrapperFixture.cpp';object='LowerPitchWrapperFixture.obj';tool='cl';flags=$flags},
 @{source='PlaybackStockFixture.asm';object='PlaybackStockFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi /DRIZ_STOCK_LOWER_PITCH_PROFILE'},
 @{source='PlaybackSourceCaller.asm';object='PlaybackSourceCaller.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='ProgressPlayerWriterFixture.cpp';object='ProgressPlayerWriterFixture.obj';tool='cl';flags=$flags},
 @{source='ProgressPlayerWriterFixture.asm';object='ProgressPlayerWriterFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='ProgressResourceWriterFixture.cpp';object='ProgressResourceWriterFixture.obj';tool='cl';flags=$flags},
 @{source='ProgressResourceWriterFixture.asm';object='ProgressResourceWriterFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='StartResourceInitializationFixture.cpp';object='StartResourceInitializationFixture.obj';tool='cl';flags=$flags},
 @{source='StartResourceInitializationFixture.asm';object='StartResourceInitializationFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'})
$lowerUnits=@(
 @{source='LowerPitchEngineFixture.cpp';object='LowerPitchEngineFixture.obj';tool='cl';flags=$flags},
 @{source='LowerPitchEngineFixture.asm';object='LowerPitchEngineFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'})
$inputs=@($units.source)+@($lowerUnits.source)+@('PlaybackStockFixture.h','StartInitializationFixture.h','ProgressResourceFixture.h')
foreach($name in $inputs){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $frozen $name)}
$sources=@(Get-ChildItem -LiteralPath $frozen -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%')
foreach($unit in $units){$commands+="$($unit.tool) $($unit.flags) /Fo`"$output/$($unit.object)`" `"$frozen/$($unit.source)`"";$commands+='if errorlevel 1 exit /b %errorlevel%'}
$exports=@('StockAudioPlayHelper','StockAudioPlayHelperEnd','StockAudioPlayDelayed','StockAudioPlayDelayedEnd','StockAudioPlay','StockAudioPlayEnd','StockAudioStop','StockAudioStopEnd','StockAudioStopBoolean','StockAudioStopBooleanEnd','StockAudioSetLoop','StockAudioSetLoopEnd','StockGetTime','StockGetTimeEnd','StockSetPitch','StockSetPitchEnd','StockPause','StockPauseEnd','StockReplay','StockReplayEnd','StockResume','StockResumeEnd','StockPlayScheduled','StockPlayScheduledEnd','StockSetClip','StockSetClipEnd','StockSetTime','StockSetTimeEnd','StockProgressPlay','StockProgressPlayEnd','StockPauseLeaf','StockPauseLeafEnd','StockFixtureMachineInvoke','StockProgressMusicPlayer','StockProgressMusicPlayerEnd','StockProgressClip','StockProgressClipEnd','StockStartMoveNext','StockStartMoveNextEnd','StockStartClipReturned','StockStartResourceLoaded','StockStartResourceCall','StockStartResourceReturned')|ForEach-Object{"/EXPORT:$_"}
$objects=$units|ForEach-Object{"`"$output/$($_.object)`""}
$link='/nologo /DLL /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF /Brepro'
$commands+="link $link $($exports -join ' ') /EXPORT:StockExecutableDir64Anchor,DATA $($objects -join ' ') /OUT:`"$output/RizToolbox.PlaybackStockFixture.dll`" /PDB:`"$output/PlaybackStockFixture.pdb`" /IMPLIB:`"$output/PlaybackStockFixture.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($unit in $lowerUnits){$commands+="$($unit.tool) $($unit.flags) /Fo`"$output/$($unit.object)`" `"$frozen/$($unit.source)`"";$commands+='if errorlevel 1 exit /b %errorlevel%'}
$lowerObjects=$lowerUnits|ForEach-Object{"`"$output/$($_.object)`""}
$commands+="link $link /EXPORT:StockLowerSetPitch /EXPORT:StockLowerSetPitchEnd /EXPORT:StockLowerExecutableDir64Anchor,DATA $($lowerObjects -join ' ') `"$output/PlaybackStockFixture.lib`" /OUT:`"$output/RizToolbox.PlaybackLowerPitchFixture.dll`" /PDB:`"$output/PlaybackLowerPitchFixture.pdb`" /IMPLIB:`"$output/PlaybackLowerPitchFixture.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($kind in @('headers','imports','exports','relocations','disasm','unwindinfo','pdbpath:verbose')){
 $commands+="dumpbin /nologo /$kind `"$output/RizToolbox.PlaybackLowerPitchFixture.dll`" > `"$output/RizToolbox.PlaybackLowerPitchFixture.$($kind.Replace(':','-')).txt`"";$commands+='if errorlevel 1 exit /b %errorlevel%'
}
foreach($kind in @('headers','imports','exports','relocations','disasm','unwindinfo','pdbpath:verbose')){
 $commands+="dumpbin /nologo /$kind `"$output/RizToolbox.PlaybackStockFixture.dll`" > `"$output/RizToolbox.PlaybackStockFixture.$($kind.Replace(':','-')).txt`"";$commands+='if errorlevel 1 exit /b %errorlevel%'
}
[IO.File]::WriteAllLines((Join-Path $output 'build.cmd'),$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-two-image-lower-pitch-fixture-build';profile='owned-lower-pitch-wrapper-and-icall-v1';images=2;wrapperCachedPtr=16;classStaticFields=184;instanceIdOffset=8;sourcePitch=24;sourceTime=32;sourceClip=48;clipLength=24;sourceRoles=20;physicalEntries=21;metadataDeclarations=21;passed=$false;actualExitCode=$null;translationUnits=$units;sources=$sources;files=@();recipe=Fact $PSCommandPath;productionAdmission=$false;gameExecuted=$false;installed=$false}
try{
 & $env:ComSpec /d /c (Join-Path $output 'build.cmd') 1> (Join-Path $output 'build.stdout.log') 2> (Join-Path $output 'build.stderr.log')
 $result.actualExitCode=$LASTEXITCODE
 [IO.File]::WriteAllText((Join-Path $output 'build.exit.txt'),[string]$LASTEXITCODE)
 if($LASTEXITCODE -ne 0){throw 'Actual owned Unity-backed fixture compilation failed.'}
 foreach($row in $sources){if((Fact $row.path).sha256 -cne $row.sha256){throw 'Frozen fixture source changed.'}}
 $result.passed=$true
}finally{
 $result.files=@(Get-ChildItem -LiteralPath $output -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
 [IO.File]::WriteAllText((Join-Path $output 'build-result.json'),($result|ConvertTo-Json -Depth 14),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Actual independent owned two-image lower-pitch fixture built.'
