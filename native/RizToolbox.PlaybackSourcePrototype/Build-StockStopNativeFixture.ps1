#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars='C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat')
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh evidence directory; retain failures.'}
if(($output+$VcVars) -match '[\r\n"%&|<>^]'){throw 'Unsupported native build path characters.'}
function Fact([string]$Path){
    $p=Get-Item -LiteralPath $Path
    [ordered]@{path=$p.FullName;sha256=(Get-FileHash -LiteralPath $p.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$p.Length}
}
$null=New-Item -ItemType Directory -Path $output
$frozen=Join-Path $output 'frozen-source'
$null=New-Item -ItemType Directory -Path $frozen
$sources=@('StockStopNativeFixture.cpp','StockStopNativeCaller.asm','PlaybackSourceContext.h') | ForEach-Object {
    $from=Join-Path $PSScriptRoot $_;$to=Join-Path $frozen $_
    Copy-Item -LiteralPath $from -Destination $to
    if((Fact $from).sha256 -cne (Fact $to).sha256){throw 'Frozen fixture input differs.'}
    Fact $to
}
$flags='/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b %errorlevel%',
    "cl $flags /Fo`"$output/StockStopNativeFixture.obj`" `"$frozen/StockStopNativeFixture.cpp`"",'if errorlevel 1 exit /b %errorlevel%',
    "ml64 /nologo /c /Zi /Fo`"$output/StockStopNativeCaller.obj`" `"$frozen/StockStopNativeCaller.asm`"",'if errorlevel 1 exit /b %errorlevel%',
    "link /nologo /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$output/StockStopNativeFixture.obj`" `"$output/StockStopNativeCaller.obj`" /OUT:`"$output/StockStopNativeFixture.exe`" /PDB:`"$output/StockStopNativeFixture.pdb`"",'if errorlevel 1 exit /b %errorlevel%')
$recipe=Join-Path $output 'build.cmd'
[IO.File]::WriteAllLines($recipe,$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-exact-stock-stop-native-fixture-build';passed=$false;actualExitCode=$null;
    sources=@($sources);builder=Fact $PSCommandPath;recipe=Fact $recipe;gameExecuted=$false;productionAdmission=$false}
try{
    & $env:ComSpec /d /c $recipe 1>(Join-Path $output 'build.stdout.log') 2>(Join-Path $output 'build.stderr.log')
    $result.actualExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $output 'build.exit.txt'),[string]$LASTEXITCODE)
    if($LASTEXITCODE -ne 0){throw 'Actual StockStop fixture compilation failed.'}
    $result.passed=$true
}finally{
    $result.files=@(Get-ChildItem -LiteralPath $output -File | Sort-Object FullName | ForEach-Object {Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $output 'build-result.json'),($result|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Owned StockStop fixture built; no game execution.'
