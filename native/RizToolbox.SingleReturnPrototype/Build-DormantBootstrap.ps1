#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $root.StartsWith('D:\riz-build-cache-next\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)){throw 'Fresh D-cache output required.'}
$null=New-Item -ItemType Directory -Path $root
$source=Join-Path $root 'source';$null=New-Item -ItemType Directory -Path $source
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=Hash $p.FullName;bytes=$p.Length}}
$sources=@('SingleReturnContext.cpp','SingleReturnContext.h','SingleReturnHandoff.inc','DormantBootstrapFixture.cpp','Build-DormantBootstrap.ps1'|ForEach-Object{
    $path=Join-Path $PSScriptRoot $_;Copy-Item -LiteralPath $path -Destination (Join-Path $source $_);Fact (Join-Path $source $_)
})
$jointHeader=Join-Path (Split-Path -Parent $PSScriptRoot) 'PlaybackLogicalHandoff.h'
Copy-Item -LiteralPath $jointHeader -Destination (Join-Path $root 'PlaybackLogicalHandoff.h')
$sources+=Fact (Join-Path $root 'PlaybackLogicalHandoff.h')
$commands=@('@echo off','chcp 65001 >nul','call "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul','if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf /DRIZ_SINGLE_RETURN_PROTOTYPE_BUILD /Fo`"$root\SingleReturnContext.obj`" `"$source\SingleReturnContext.cpp`" /link /OUT:`"$root\RizToolbox.SingleReturn.Prototype.dll`" /PDB:`"$root\SingleReturn.Prototype.pdb`" /IMPLIB:`"$root\SingleReturn.Prototype.lib`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf /Fo`"$root\DormantBootstrapFixture.obj`" `"$source\DormantBootstrapFixture.cpp`" /link /OUT:`"$root\OwnedDormantBootstrap.Fixture.exe`" /PDB:`"$root\OwnedDormantBootstrap.Fixture.pdb`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$root\RizToolbox.SingleReturn.Prototype.dll`" >`"$root\exports.txt`"",'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$root\RizToolbox.SingleReturn.Prototype.dll`" >`"$root\unwind.txt`"",'if errorlevel 1 exit /b 1')
[IO.File]::WriteAllLines((Join-Path $root 'build.cmd'),$commands,[Text.UTF8Encoding]::new($false))
$result=[ordered]@{kind='owned-native-dormant-bootstrap-build';passed=$false;buildExitCode=$null;sources=$sources;files=@();gameExecuted=$false;installed=$false;productionAdmission=$false}
try{
    & $env:ComSpec /d /c (Join-Path $root 'build.cmd') 1> (Join-Path $root 'build.stdout.log') 2> (Join-Path $root 'build.stderr.log')
    $result.buildExitCode=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $root 'build.exit.txt'),[string]$LASTEXITCODE)
    if($result.buildExitCode -ne 0){throw 'Actual native compiler/linker failed.'}
    foreach($row in $sources){if((Hash $row.path) -cne $row.sha256){throw 'Frozen native source changed.'}}
    $result.passed=$true
}finally{
    $result.files=@(Get-ChildItem -LiteralPath $root -File|ForEach-Object{Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $root 'native-manifest.json'),($result|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
}
Write-Output 'Native dormant bootstrap helper and owned fixture built successfully.'
