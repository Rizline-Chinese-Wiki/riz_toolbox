#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output=[IO.Path]::GetFullPath($OutputDirectory)
$allowed=[IO.Path]::GetFullPath((Join-Path $repo '..'))+[IO.Path]::DirectorySeparatorChar
if(-not $output.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh evidence directory beside the isolated source.'}
if(($output+$VcVars) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path.'}
function Fact([string]$Path){$file=Get-Item -LiteralPath $Path;[ordered]@{path=$file.FullName;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}}
$inputs=@('native/PlaybackLogicalHandoff.h','native/PlaybackInitialization.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp','native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackPlayerWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackUnityBackingWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackLowerPitch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp','native/RizToolbox.SingleReturnPrototype/SingleReturnContext.h',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnHandoff.inc','native/RizToolbox.SingleReturnPrototype/SingleReturnInitialCapture.inc','native/RizToolbox.SingleReturnPrototype/SingleReturnLowerPitch.inc',
    'scripts/Build-UnityBackingNative.ps1')
$null=New-Item -ItemType Directory -Path $output
$frozen=Join-Path $output 'frozen-source'
$sources=@($inputs|ForEach-Object{
    $destination=Join-Path $frozen $_
    $null=New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
    Copy-Item -LiteralPath (Join-Path $repo $_) -Destination $destination
    $actual=Fact $destination
    if($actual.sha256 -cne (Fact (Join-Path $repo $_)).sha256){throw 'Native freeze differs.'}
    $actual
})
$flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$link='/nologo /DLL /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%')
$commands+="cl $flags /Fo`"$output/PlaybackSourceContext.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="ml64 /nologo /c /Zi /Fo`"$output/PlaybackWriterCall.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="cl $flags /DRIZ_SINGLE_RETURN_PROTOTYPE_BUILD /Fo`"$output/SingleReturnContext.obj`" `"$frozen/native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="link $link `"$output/PlaybackSourceContext.obj`" `"$output/PlaybackWriterCall.obj`" /OUT:`"$output/RizToolbox.PlaybackSourcePrototype.dll`" /PDB:`"$output/PlaybackSourcePrototype.pdb`" /IMPLIB:`"$output/PlaybackSourcePrototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="link $link `"$output/SingleReturnContext.obj`" /OUT:`"$output/RizToolbox.SingleReturn.Prototype.dll`" /PDB:`"$output/SingleReturn.Prototype.pdb`" /IMPLIB:`"$output/SingleReturn.Prototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($file in @('RizToolbox.PlaybackSourcePrototype.dll','RizToolbox.SingleReturn.Prototype.dll')){
    foreach($kind in @('headers','exports','imports','unwindinfo','pdbpath:verbose')){
        $name=$kind.Replace(':','-')
        $commands+="dumpbin /nologo /$kind `"$output/$file`" > `"$output/$file.$name.txt`""
        $commands+='if errorlevel 1 exit /b %errorlevel%'
    }
}
$recipe=Join-Path $output 'build.cmd'
[IO.File]::WriteAllLines($recipe,$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-unity-backing-native-build';passed=$false;actualExitCode=$null;sources=$sources;compilerFlags=$flags;
    unityBackingWatchVersion=1;unityBackingWatchBytes=128;sourceVersion=4;sourceFrameBytes=168;sourceInfoBytes=136;
    gameExecuted=$false;installed=$false;productionAdmission=$false}
try{
    & $env:ComSpec /d /c $recipe 1>(Join-Path $output 'build.stdout.log') 2>(Join-Path $output 'build.stderr.log')
    $result.actualExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $output 'build.exit.txt'),[string]$LASTEXITCODE)
    if($LASTEXITCODE -ne 0){throw 'Actual Unity backing native build failed.'}
    foreach($row in $sources){if((Fact $row.path).sha256 -cne $row.sha256){throw 'Frozen source changed.'}}
    $result.passed=$true
}finally{
    $result.files=@(Get-ChildItem -LiteralPath $output -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $output 'native-build-result.json'),($result|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Owned Unity backing native helpers built.'
