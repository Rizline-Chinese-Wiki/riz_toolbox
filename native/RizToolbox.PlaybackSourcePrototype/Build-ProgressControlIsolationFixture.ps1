#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars='C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat'
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh fixture output directory; retain previous failures.'}
if(($output+$VcVars) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path characters.'}
function Fact([string]$Path){
    $item=Get-Item -LiteralPath $Path
    [ordered]@{path=$item.FullName;sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$item.Length}
}
$null=New-Item -ItemType Directory -Path $output
$frozen=Join-Path $output 'frozen-source'
$null=New-Item -ItemType Directory -Path $frozen
$sources=@('ProgressControlIsolationFixture.cpp','PlaybackSourceContext.h','../PlaybackInitialization.h') | ForEach-Object {
    $source=Join-Path $PSScriptRoot $_;$target=Join-Path $frozen $_
    Copy-Item -LiteralPath $source -Destination $target
    if((Fact $source).sha256 -cne (Fact $target).sha256){throw 'Frozen fixture source copy differs.'}
    Fact $target
}
$recipe=Join-Path $output 'build.cmd'
$flags='/nologo /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%',
    "cl $flags /Fo`"$output/ProgressControlIsolationFixture.obj`" /Fe`"$output/ProgressControlIsolationFixture.exe`" `"$frozen/ProgressControlIsolationFixture.cpp`" /link /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF /PDB:`"$output/ProgressControlIsolationFixture.pdb`"",
    'if errorlevel 1 exit /b %errorlevel%')
[IO.File]::WriteAllLines($recipe,$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-progress-control-isolation-fixture-build';passed=$false;actualExitCode=$null;
    compilerFlags=$flags;sources=$sources;builder=Fact $PSCommandPath;recipe=Fact $recipe;
    gameExecuted=$false;installed=$false;productionAdmission=$false}
try{
    & $env:ComSpec /d /c $recipe 1>(Join-Path $output 'build.stdout.log') 2>(Join-Path $output 'build.stderr.log')
    $result.actualExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $output 'build.exit.txt'),[string]$LASTEXITCODE)
    if($LASTEXITCODE -ne 0){throw 'Actual owned fixture compilation failed.'}
    if(@($sources | Where-Object {(Fact $_.path).sha256 -cne $_.sha256}).Count){throw 'Frozen fixture source changed.'}
    $result.passed=$true
}finally{
    $result.files=@(Get-ChildItem -LiteralPath $output -File | Sort-Object FullName | ForEach-Object {Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $output 'build-result.json'),($result | ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Owned Progress isolation fixture built; no game code executed.'
