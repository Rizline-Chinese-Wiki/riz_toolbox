#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
 [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=[IO.Path]::GetFullPath((Join-Path $repo '..'))+[IO.Path]::DirectorySeparatorChar
if(-not $output.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh directory in this continuation.'}
if(($output+$VcVars) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path characters.'}
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=(Get-FileHash -LiteralPath $p.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$p.Length}}
$null=New-Item -ItemType Directory -Path $output
$frozen=Join-Path $output 'frozen-source'
$null=New-Item -ItemType Directory -Path $frozen
Copy-Item -LiteralPath (Join-Path $repo 'native') -Destination (Join-Path $frozen 'native') -Recurse
$sources=@(Get-ChildItem -LiteralPath (Join-Path $frozen 'native') -Recurse -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$units=@(
 @{source='native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp';object='SingleReturnContext.obj';tool='cl';flags=$flags+' /DRIZ_SINGLE_RETURN_PROTOTYPE_BUILD'},
 @{source='native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp';object='PlaybackSourceContext.obj';tool='cl';flags=$flags},
 @{source='native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm';object='PlaybackWriterCall.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp';object='PlaybackStockFixture.obj';tool='cl';flags=$flags},
 @{source='native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm';object='PlaybackStockFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm';object='PlaybackSourceCaller.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.cpp';object='ProgressPlayerWriterFixture.obj';tool='cl';flags=$flags},
 @{source='native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.asm';object='ProgressPlayerWriterFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'},
 @{source='native/RizToolbox.PlaybackStockFixture/StartInitializationFixture.cpp';object='StartInitializationFixture.obj';tool='cl';flags=$flags},
 @{source='native/RizToolbox.PlaybackStockFixture/StartInitializationFixture.asm';object='StartInitializationFixture.asm.obj';tool='ml64';flags='/nologo /c /Zi'})
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%')
foreach($unit in $units){$commands+="$($unit.tool) $($unit.flags) /Fo`"$output/$($unit.object)`" `"$frozen/$($unit.source)`"";$commands+='if errorlevel 1 exit /b %errorlevel%'}
$link='/nologo /DLL /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands+="link $link `"$output/SingleReturnContext.obj`" /OUT:`"$output/RizToolbox.SingleReturn.Prototype.dll`" /PDB:`"$output/SingleReturn.Prototype.pdb`" /IMPLIB:`"$output/SingleReturn.Prototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="link $link `"$output/PlaybackSourceContext.obj`" `"$output/PlaybackWriterCall.obj`" /OUT:`"$output/RizToolbox.PlaybackSourcePrototype.dll`" /PDB:`"$output/PlaybackSourcePrototype.pdb`" /IMPLIB:`"$output/PlaybackSourcePrototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$exports=@('StockAudioPlayHelper','StockAudioPlayHelperEnd','StockAudioPlayDelayed','StockAudioPlayDelayedEnd','StockAudioPlay','StockAudioPlayEnd','StockAudioStop','StockAudioStopEnd','StockAudioStopBoolean','StockAudioStopBooleanEnd','StockAudioSetLoop','StockAudioSetLoopEnd','StockGetTime','StockGetTimeEnd','StockSetPitch','StockSetPitchEnd','StockPause','StockPauseEnd','StockReplay','StockReplayEnd','StockResume','StockResumeEnd','StockPlayScheduled','StockPlayScheduledEnd','StockSetClip','StockSetClipEnd','StockSetTime','StockSetTimeEnd','StockProgressPlay','StockProgressPlayEnd','StockPauseLeaf','StockPauseLeafEnd','StockFixtureMachineInvoke','StockProgressMusicPlayer','StockProgressMusicPlayerEnd','StockStartMoveNext','StockStartMoveNextEnd','StockStartClipReturned')|ForEach-Object{"/EXPORT:$_"}
$commands+="link $link $($exports -join ' ') /EXPORT:StockExecutableDir64Anchor,DATA `"$output/PlaybackStockFixture.obj`" `"$output/PlaybackStockFixture.asm.obj`" `"$output/PlaybackSourceCaller.asm.obj`" `"$output/ProgressPlayerWriterFixture.obj`" `"$output/ProgressPlayerWriterFixture.asm.obj`" `"$output/StartInitializationFixture.obj`" `"$output/StartInitializationFixture.asm.obj`" /OUT:`"$output/RizToolbox.PlaybackStockFixture.dll`" /PDB:`"$output/PlaybackStockFixture.pdb`" /IMPLIB:`"$output/PlaybackStockFixture.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($name in @('RizToolbox.SingleReturn.Prototype','RizToolbox.PlaybackSourcePrototype','RizToolbox.PlaybackStockFixture')){
 foreach($kind in @('headers','imports','exports','relocations','disasm','unwindinfo','pdbpath:verbose')){
  $commands+="dumpbin /nologo /$kind `"$output/$name.dll`" > `"$output/$name.$($kind.Replace(':','-')).txt`"";$commands+='if errorlevel 1 exit /b %errorlevel%'
 }
}
[IO.File]::WriteAllLines((Join-Path $output 'build.cmd'),$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-direct-audio-stock-fixture-build';sourceVersion=4;frameBytes=168;infoBytes=136;writerVersion=1;initializationProtocol=2;passed=$false;actualExitCode=$null;translationUnits=$units;sources=$sources;files=@();recipe=Fact $PSCommandPath;productionAdmission=$false;gameExecuted=$false;installed=$false}
try{
 & $env:ComSpec /d /c (Join-Path $output 'build.cmd') 1> (Join-Path $output 'build.stdout.log') 2> (Join-Path $output 'build.stderr.log')
 $result.actualExitCode=$LASTEXITCODE
 [IO.File]::WriteAllText((Join-Path $output 'build.exit.txt'),[string]$LASTEXITCODE)
 if($LASTEXITCODE -ne 0){throw 'Actual owned Start helper/fixture compilation failed.'}
 foreach($row in $sources){if((Fact $row.path).sha256 -cne $row.sha256){throw 'Frozen native source changed.'}}
 $result.passed=$true
}finally{
 $result.files=@(Get-ChildItem -LiteralPath $output -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
 [IO.File]::WriteAllText((Join-Path $output 'build-result.json'),($result|ConvertTo-Json -Depth 14),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Actual SourceV4, SingleReturn, and owned Start fixture built.'
