#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$BaselineProxy='D:/SteamLibrary/steamapps/common/Rizline/winhttp.dll',
    [string]$Python = '',
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
$fixture=Join-Path $repo 'native/RizToolbox.WinHttpColdFixture'
$prepare=Join-Path $repo 'docs/validation/foundation-doorstop-cold-20261025/prepare-cold-contract.py'
foreach($path in @($output,$fixture,$prepare,$BaselineProxy,$Python,$VcVars)){
    if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported compiler path metacharacter.'}
}
if(Test-Path -LiteralPath (Join-Path $output 'cold-build-manifest.json')){throw 'Preserve the existing completed cold build.'}
New-Item -ItemType Directory -Path $output -Force|Out-Null
$doorstop=Join-Path $output 'doorstop'
& (Join-Path $PSScriptRoot 'Build-Doorstop.ps1') -OutputDirectory $doorstop -FixedSystemWinHttp
Copy-Item -LiteralPath (Join-Path $doorstop 'winhttp.dll') -Destination (Join-Path $output 'winhttp.dll')
$contract=Join-Path $output 'cold-contract.json'
$header=Join-Path $output 'ColdProxySymbols.h'
& $Python $prepare --proxy (Join-Path $output 'winhttp.dll') --map (Join-Path $doorstop 'doorstop.map') --baseline $BaselineProxy --header $header --output $contract
if($LASTEXITCODE -ne 0){throw 'Production cold caller contract failed.'}
$utf8=[Text.UTF8Encoding]::new($false)
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
$common='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands+=@(
    "cl $common /LD /I`"$output`" /Fo`"$output\ColdImporter.obj`" /Fd`"$output\ColdImporter.compiler.pdb`" `"$fixture\Importer.cpp`" /link winhttp.lib /OUT:`"$output\WinHttpCold.Importer.dll`" /IMPLIB:`"$output\WinHttpCold.Importer.lib`" /PDB:`"$output\ColdImporter.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /I`"$output`" /Fo`"$output\ColdHost.obj`" /Fd`"$output\ColdHost.compiler.pdb`" `"$fixture\Host.cpp`" /link `"$output\WinHttpCold.Importer.lib`" /OUT:`"$output\WinHttpCold.Host.exe`" /PDB:`"$output\ColdHost.pdb`" $link",'if errorlevel 1 exit /b 1')
$files=@('winhttp.dll','WinHttpCold.Importer.dll','WinHttpCold.Host.exe')
foreach($name in $files){$path=Join-Path $output $name;$commands+="dumpbin /nologo /headers /imports /exports /loadconfig /unwindinfo `"$path`" > `"$path.pe.txt`"";$commands+='if errorlevel 1 exit /b 1'}
[IO.File]::WriteAllLines((Join-Path $output 'build-cold.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-cold.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'cold-build.log')
if($LASTEXITCODE -ne 0){throw "Production cold caller build failed: $LASTEXITCODE"}
# Disable the existing CLR path in the self-owned executable. This file is part
# of the exact child inputs; it is never copied into the real game directory.
[IO.File]::WriteAllText((Join-Path $output 'doorstop_config.ini'),"[General]`nenabled=false`n",$utf8)
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$sources=@(
    'scripts/Build-WinHttpColdFixture.ps1','scripts/Build-Doorstop.ps1',
    'native/RizToolbox.Doorstop/riz_winhttp.h','native/RizToolbox.Doorstop/riz_winhttp.c','native/RizToolbox.Doorstop/riz_winhttp_entry.asm',
    'native/RizToolbox.Doorstop/riz_winhttp_doorstop.h','native/RizToolbox.Doorstop/riz_winhttp_doorstop.c',
    'native/RizToolbox.Doorstop/riz_preinit.c','native/RizToolbox.Doorstop/riz_preinit.h',
    'native/RizToolbox.Doorstop/riz_init_call.c','native/RizToolbox.Doorstop/riz_init_call.h',
    'native/RizToolbox.Doorstop/upstream.json','native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md',
    'native/RizToolbox.WinHttpColdFixture/ColdApi.h','native/RizToolbox.WinHttpColdFixture/Importer.cpp','native/RizToolbox.WinHttpColdFixture/Host.cpp',
    'native/RizToolbox.WinHttpFixture/WinHttpFixtureApi.h',
    'docs/validation/foundation-doorstop-cold-20261025/prepare-cold-contract.py',
    'docs/validation/foundation-winhttp-forward-20261025/verify-winhttp-forward.py',
    'docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py')
$rows=@($files+@('doorstop/doorstop.map','ColdProxySymbols.h','cold-contract.json','doorstop_config.ini','doorstop/doorstop-manifest.json')|ForEach-Object{[ordered]@{path=(Join-Path $output $_);sha256=(Hash (Join-Path $output $_))}})
$sourceRows=@($sources|ForEach-Object{[ordered]@{path=(Join-Path $repo $_);sha256=(Hash (Join-Path $repo $_))}})
[ordered]@{kind='production-doorstop-cold-build';schemaVersion=1;files=$rows;sources=$sourceRows;
    baselineProxy=[ordered]@{path=[IO.Path]::GetFullPath($BaselineProxy);sha256=(Hash $BaselineProxy)};
    fixedSystemWinHttp=$true;publicExports=82;dllEntryProfile=$true;fixtureExecuted=$false;gameExecuted=$false;installed=$false}|
    ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $output 'cold-build-manifest.json') -Encoding utf8
Write-Output "Production Doorstop cold fixture built: $output"
