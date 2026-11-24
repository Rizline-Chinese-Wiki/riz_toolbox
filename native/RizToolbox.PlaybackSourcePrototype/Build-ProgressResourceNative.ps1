#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars='C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat'
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$root=[IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$allowed=[IO.Path]::GetFullPath((Join-Path $sourceRoot '..'))+[IO.Path]::DirectorySeparatorChar
if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Use a fresh evidence directory beside the isolated source.'}
if(($root+$VcVars) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path characters.'}
function Fact([string]$Path){
    $item=Get-Item -LiteralPath $Path
    [ordered]@{path=$item.FullName;sha256=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$item.Length}
}
$null=New-Item -ItemType Directory -Path $root
$frozen=Join-Path $root 'frozen-source'
$null=New-Item -ItemType Directory -Path $frozen
$inputs=@('native/PlaybackLogicalHandoff.h','native/PlaybackInitialization.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp','native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackPlayerWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackUnityBackingWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackLowerPitch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm','native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.cpp',
    'native/RizToolbox.PlaybackSourcePrototype/ProgressResourceNativeFixture.asm','native/RizToolbox.PlaybackSourcePrototype/Build-ProgressResourceNative.ps1')
$sources=@($inputs | ForEach-Object {
    $target=Join-Path $frozen $_
    $null=New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target)
    Copy-Item -LiteralPath (Join-Path $sourceRoot $_) -Destination $target
    if((Fact (Join-Path $sourceRoot $_)).sha256 -cne (Fact $target).sha256){throw 'Frozen native input differs.'}
    Fact $target
})
$flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$link='/nologo /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%')
foreach($unit in @('PlaybackSourceContext','ProgressResourceNativeFixture')){
    $commands+="cl $flags /Fo`"$root/$unit.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/$unit.cpp`""
    $commands+='if errorlevel 1 exit /b %errorlevel%'
}
foreach($unit in @('PlaybackWriterCall','ProgressResourceNativeFixture')){
    $suffix=if($unit -eq 'ProgressResourceNativeFixture'){'Asm'}else{''}
    $commands+="ml64 /nologo /c /Zi /Fo`"$root/$unit$suffix.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/$unit.asm`""
    $commands+='if errorlevel 1 exit /b %errorlevel%'
}
$commands+="link $link /DLL `"$root/PlaybackSourceContext.obj`" `"$root/PlaybackWriterCall.obj`" /OUT:`"$root/RizToolbox.PlaybackSourcePrototype.dll`" /PDB:`"$root/PlaybackSourcePrototype.pdb`" /IMPLIB:`"$root/PlaybackSourcePrototype.lib`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
$commands+="link $link `"$root/ProgressResourceNativeFixture.obj`" `"$root/ProgressResourceNativeFixtureAsm.obj`" /OUT:`"$root/ProgressResourceNativeFixture.exe`" /PDB:`"$root/ProgressResourceNativeFixture.pdb`""
$commands+='if errorlevel 1 exit /b %errorlevel%'
foreach($file in @('RizToolbox.PlaybackSourcePrototype.dll','ProgressResourceNativeFixture.exe')){
    foreach($kind in @('headers','exports','unwindinfo')){
        $commands+="dumpbin /nologo /$kind `"$root/$file`" > `"$root/$file.$kind.txt`""
        $commands+='if errorlevel 1 exit /b %errorlevel%'
    }
}
$recipe=Join-Path $root 'build.cmd'
[IO.File]::WriteAllLines($recipe,$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-progress-resource-native-build';passed=$false;actualExitCode=$null;compilerFlags=$flags;
    sourceVersion=4;frameBytes=168;sourceInfoBytes=136;progressWatchVersion=1;resourceVersion=1;resourceBytes=112;
    sources=$sources;gameExecuted=$false;installed=$false;productionAdmission=$false}
try{
    & $env:ComSpec /d /c $recipe 1>(Join-Path $root 'build.stdout.log') 2>(Join-Path $root 'build.stderr.log')
    $result.actualExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $root 'build.exit.txt'),[string]$LASTEXITCODE)
    if($LASTEXITCODE -ne 0){throw 'Actual resource native build failed.'}
    foreach($row in $sources){if((Fact $row.path).sha256 -cne $row.sha256){throw 'Frozen native input changed.'}}
    $result.passed=$true
}finally{
    $result.files=@(Get-ChildItem -LiteralPath $root -File | Sort-Object FullName | ForEach-Object {Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $root 'native-build-result.json'),($result | ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Owned Progress resource Source helper and short fixture built.'
