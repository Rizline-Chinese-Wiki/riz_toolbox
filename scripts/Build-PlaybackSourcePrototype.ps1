#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'An absolute build path is required.'}
    $full=[IO.Path]::GetFullPath($Path)
    if($full -match '[\r\n"%&|<>^]'){throw 'Unsupported build path characters.'}
    return $full
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
$output=Absolute $OutputDirectory
$vcvarsPath=Absolute $VcVars
if(Test-Path -LiteralPath $output){throw 'Use a new output directory; failed builds are retained.'}
$relative=@('native/PlaybackInitialization.h','native/PlaybackLogicalHandoff.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackPlayerWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackUnityBackingWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackLowerPitch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceFixture.cpp',
    'native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.h',
    'native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp',
    'native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm',
    'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.cpp',
    'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.asm',
    'native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm',
    'scripts/Build-PlaybackSourcePrototype.ps1')
$inputs=@($relative|ForEach-Object {Join-Path $repo $_})+@($vcvarsPath)
foreach($path in $inputs){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing build input: $path"}}
New-Item -ItemType Directory -Path $output|Out-Null
$frozen=Join-Path $output 'frozen-source'
New-Item -ItemType Directory -Path $frozen|Out-Null
foreach($path in $relative){$destination=Join-Path $frozen $path;New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $repo $path) -Destination $destination}
$source=Join-Path $frozen 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp'
$fixture=Join-Path $frozen 'native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.cpp'
$asm=Join-Path $frozen 'native/RizToolbox.PlaybackStockFixture/PlaybackStockFixture.asm'
$callerAsm=Join-Path $frozen 'native/RizToolbox.PlaybackStockFixture/PlaybackSourceCaller.asm'
$runner=Join-Path $frozen 'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceFixture.cpp'
$flags=@('/nologo','/c','/std:c++17','/O2','/W4','/WX','/EHa','/MD','/Z7','/guard:cf')
$tu=@(
    [ordered]@{source=$source;object=(Join-Path $output 'PlaybackSourceContext.obj');flags=$flags;tool='cl'},
    [ordered]@{source=(Join-Path $frozen 'native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm');object=(Join-Path $output 'PlaybackWriterCall.obj');flags=@('/nologo','/c','/Zi');tool='ml64'},
    [ordered]@{source=$fixture;object=(Join-Path $output 'PlaybackStockFixture.obj');flags=$flags;tool='cl'},
    [ordered]@{source=(Join-Path $frozen 'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.cpp');object=(Join-Path $output 'ProgressPlayerWriterFixture.obj');flags=$flags;tool='cl'},
    [ordered]@{source=(Join-Path $frozen 'native/RizToolbox.PlaybackStockFixture/ProgressPlayerWriterFixture.asm');object=(Join-Path $output 'ProgressPlayerWriterFixture.asm.obj');flags=@('/nologo','/c','/Zi');tool='ml64'},
    [ordered]@{source=$runner;object=(Join-Path $output 'PlaybackSourceFixture.obj');flags=$flags;tool='cl'},
    [ordered]@{source=$asm;object=(Join-Path $output 'PlaybackStockFixture.asm.obj');flags=@('/nologo','/c','/Zi');tool='ml64'},
    [ordered]@{source=$callerAsm;object=(Join-Path $output 'PlaybackSourceCaller.asm.obj');flags=@('/nologo','/c','/Zi');tool='ml64'})
$manifest=[ordered]@{kind='independent-stock-shaped-source-prototype';abiVersion=4;frameSize=168;infoSize=136;initializationProtocol=2;
    gameCodeExecuted=$false;productionAdmission=$false;installed=$false;passed=$false;actualExitCode=$null;
    startedAt=[DateTime]::UtcNow.ToString('o');sources=@($inputs|ForEach-Object {[ordered]@{path=$_;sha256=Hash $_}});
    translationUnits=$tu;files=@();frozenSources=@(Get-ChildItem -LiteralPath $frozen -File -Recurse|ForEach-Object {[ordered]@{path=$_.FullName;sha256=Hash $_.FullName}})}
$manifestPath=Join-Path $output 'build-result.json';Json $manifest $manifestPath
$commands=@('@echo off',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b %errorlevel%')
foreach($unit in $tu){$commands+="$($unit.tool) $($unit.flags -join ' ') /Fo`"$($unit.object)`" `"$($unit.source)`"";$commands+='if errorlevel 1 exit /b %errorlevel%'}
$linkFlags='/DLL /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands+="link /nologo $linkFlags `"$output/PlaybackSourceContext.obj`" `"$output/PlaybackWriterCall.obj`" /OUT:`"$output/RizToolbox.PlaybackSourcePrototype.dll`" /PDB:`"$output/RizToolbox.PlaybackSourcePrototype.pdb`" /IMPLIB:`"$output/RizToolbox.PlaybackSourcePrototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="link /nologo /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$output/PlaybackSourceFixture.obj`" /OUT:`"$output/PlaybackSourceFixture.exe`" /PDB:`"$output/PlaybackSourceFixture.pdb`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$stockExports=@('StockGetTime','StockGetTimeEnd','StockSetPitch','StockSetPitchEnd','StockPause','StockPauseEnd','StockReplay','StockReplayEnd','StockResume','StockResumeEnd','StockPlayScheduled','StockPlayScheduledEnd','StockSetClip','StockSetClipEnd','StockSetTime','StockSetTimeEnd','StockProgressPlay','StockProgressPlayEnd','StockPauseLeaf','StockPauseLeafEnd','StockFixtureMachineInvoke')|ForEach-Object {"/EXPORT:$_"}
$stockExports+=@('/EXPORT:StockExecutableDir64Anchor,DATA','/EXPORT:StockProgressMusicPlayer','/EXPORT:StockProgressMusicPlayerEnd')
$commands+="link /nologo $linkFlags $($stockExports -join ' ') `"$output/PlaybackStockFixture.obj`" `"$output/PlaybackStockFixture.asm.obj`" `"$output/PlaybackSourceCaller.asm.obj`" `"$output/ProgressPlayerWriterFixture.obj`" `"$output/ProgressPlayerWriterFixture.asm.obj`" /OUT:`"$output/RizToolbox.PlaybackStockFixture.dll`" /PDB:`"$output/RizToolbox.PlaybackStockFixture.pdb`" /IMPLIB:`"$output/RizToolbox.PlaybackStockFixture.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($name in @('RizToolbox.PlaybackSourcePrototype','RizToolbox.PlaybackStockFixture')){
    foreach($kind in @('headers','imports','exports','relocations','disasm','unwindinfo','pdbpath:verbose')){
        $fileKind=$kind.Replace(':','-');$commands+="dumpbin /nologo /$kind `"$output/$name.dll`" > `"$output/$name.$fileKind.txt`"";$commands+='if errorlevel 1 exit /b %errorlevel%'
    }
}
foreach($unit in $tu){$name=[IO.Path]::GetFileName($unit.object);$commands+="dumpbin /nologo /all `"$($unit.object)`" > `"$output/$name.coff.txt`"";$commands+='if errorlevel 1 exit /b %errorlevel%'}
$batch=Join-Path $output 'build.cmd';[IO.File]::WriteAllLines($batch,$commands,[Text.UTF8Encoding]::new($false))
Push-Location $output
try {
    & $env:ComSpec /d /c $batch 1> (Join-Path $output 'build.stdout.log') 2> (Join-Path $output 'build.stderr.log')
    $manifest.actualExitCode=$LASTEXITCODE;$manifest.passed=$LASTEXITCODE -eq 0
    if(-not $manifest.passed){throw "Source prototype build failed with actual exit $LASTEXITCODE; output retained at $output"}
} finally {
    Pop-Location
    $manifest.inputsUnchanged=@($manifest.sources|Where-Object {(Hash $_.path) -ne $_.sha256}).Count -eq 0
    $manifest.frozenSourcesUnchanged=@($manifest.frozenSources|Where-Object {(Hash $_.path) -ne $_.sha256}).Count -eq 0
    $manifest.passed=$manifest.passed -and $manifest.inputsUnchanged -and $manifest.frozenSourcesUnchanged
    $manifest.completedAt=[DateTime]::UtcNow.ToString('o')
    $manifest.files=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'build-result.json'|ForEach-Object {[ordered]@{path=$_.FullName;bytes=$_.Length;sha256=Hash $_.FullName}})
    Json $manifest $manifestPath
}
Write-Output (Join-Path $output 'RizToolbox.PlaybackSourcePrototype.dll')
