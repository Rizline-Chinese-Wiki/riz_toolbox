#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repoRoot 'native/RizToolbox.Doorstop'
$fixture=Join-Path $repoRoot 'native/RizToolbox.WinHttpFixture'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($path in @($source,$fixture,$output,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported compiler path metacharacter.'}}
$sourcePaths=@((Join-Path $source 'riz_winhttp.h'),(Join-Path $source 'riz_winhttp.c'),(Join-Path $source 'riz_winhttp_entry.asm'),
    (Join-Path $fixture 'WinHttpForwardFixture.cpp'),(Join-Path $fixture 'Importer.cpp'),(Join-Path $fixture 'WinHttpFixtureApi.h'),$PSCommandPath)
foreach($path in $sourcePaths+@($VcVars)){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing WinHTTP input: $path"}}
if(Test-Path -LiteralPath (Join-Path $output 'fixture-build-manifest.json')){throw 'Preserve the earlier completed build; choose a fresh output directory.'}
New-Item -ItemType Directory -Path $output -Force|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
$files=[Collections.Generic.List[object]]::new()
foreach($profile in @('production','fixture')){
    $directory=Join-Path $output $profile
    New-Item -ItemType Directory -Path $directory -Force|Out-Null
    # C exports its three APIs (fixture adds its setter) and names the four
    # MASM tail exports through linker directives. Do not duplicate those in
    # the DEF; the independent PE verifier requires exactly seven/eight.
    $exports=@('LIBRARY RizToolbox.WinHttpForward.dll')
    $define=if($profile -eq 'fixture'){'/DRIZ_WINHTTP_FIXTURE=1'}else{''}
    [IO.File]::WriteAllLines((Join-Path $directory 'forward.def'),$exports,$utf8)
    [IO.File]::WriteAllLines((Join-Path $directory 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),$utf8)
    $commands+=@(
        "lib /nologo /machine:x64 /def:`"$directory\seh.def`" /out:`"$directory\seh.lib`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /c /TC /std:c11 /Zc:preprocessor- /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi $define /Fo`"$directory\riz_winhttp.obj`" /Fd`"$directory\riz_winhttp.compiler.pdb`" `"$source\riz_winhttp.c`"",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$directory\riz_winhttp_entry.obj`" `"$source\riz_winhttp_entry.asm`"",'if errorlevel 1 exit /b 1',
        "link /nologo /DLL /NOENTRY /NODEFAULTLIB /MACHINE:X64 /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /DEF:`"$directory\forward.def`" `"$directory\riz_winhttp.obj`" `"$directory\riz_winhttp_entry.obj`" `"$directory\seh.lib`" kernel32.lib /OUT:`"$directory\RizToolbox.WinHttpForward.dll`" /IMPLIB:`"$directory\RizToolbox.WinHttpForward.lib`" /PDB:`"$directory\RizToolbox.WinHttpForward.pdb`"",'if errorlevel 1 exit /b 1')
    $files.Add(@{name='RizToolbox.WinHttpForward.dll';path=(Join-Path $directory 'RizToolbox.WinHttpForward.dll');profile=$profile;role='forward'})
}
$common='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands+=@(
    "cl $common /LD /Fo`"$output\WinHttpOwnedImporter.obj`" /Fd`"$output\WinHttpOwnedImporter.compiler.pdb`" `"$fixture\Importer.cpp`" /link `"$output\production\RizToolbox.WinHttpForward.lib`" /OUT:`"$output\RizWinHttpOwnedImporter.dll`" /IMPLIB:`"$output\RizWinHttpOwnedImporter.lib`" /PDB:`"$output\WinHttpOwnedImporter.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /DRIZ_WINHTTP_FIXTURE=1 /Fo`"$output\WinHttpForwardFixture.obj`" /Fd`"$output\WinHttpForwardFixture.compiler.pdb`" `"$fixture\WinHttpForwardFixture.cpp`" /link /OUT:`"$output\WinHttpForward.Fixture.exe`" /PDB:`"$output\WinHttpForwardFixture.pdb`" $link",'if errorlevel 1 exit /b 1')
$files.Add(@{name='RizWinHttpOwnedImporter.dll';path=(Join-Path $output 'RizWinHttpOwnedImporter.dll');role='importer';profile='shared'})
$files.Add(@{name='WinHttpForward.Fixture.exe';path=(Join-Path $output 'WinHttpForward.Fixture.exe');role='host';profile='shared'})
foreach($row in $files){$path=$row.path;$commands+="dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$path`" > `"$path.pe.txt`"";$commands+='if errorlevel 1 exit /b 1'}
[IO.File]::WriteAllLines((Join-Path $output 'build-winhttp-forward.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-winhttp-forward.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Fixed WinHTTP build failed: $LASTEXITCODE"}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
foreach($row in $files){$row.sha256=Hash $row.path}
$inputs=@($sourcePaths|ForEach-Object{@{path=$_;sha256=(Hash $_)}})
[ordered]@{schemaVersion=1;kind='fixed-winhttp-forward-build';files=@($files);sources=$inputs;compilerEnvironment=$VcVars;
    fixtureExecuted=$false;systemCodeExecuted=$false;gameExecuted=$false;installed=$false;loaderStageAdopted=$false}|
    ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Fixed WinHTTP forward build: $output"
