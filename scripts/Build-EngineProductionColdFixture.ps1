#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$SystemWinHttp=(Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'),
    [string]$Python = '',
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($CandidateDirectory) -or -not [IO.Path]::IsPathFullyQualified($OutputDirectory)){
    throw 'Absolute candidate and new output directories are required.'
}
$candidate=(Resolve-Path -LiteralPath $CandidateDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve earlier cold builds; use a fresh output directory.'}
$fixture=Join-Path $repo 'native/RizToolbox.EngineProductionFixture'
$prepare=Join-Path $repo 'docs/validation/foundation-engine-production-20261025/prepare-production-cold.py'
foreach($path in @($candidate,$output,$repo,$fixture,$Python,$VcVars,$SystemWinHttp)){
    if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported compiler path metacharacter.'}
}
if($output.StartsWith('D:\',[StringComparison]::OrdinalIgnoreCase)){throw 'This owned fixture must not write into the game drive.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$sourcePaths=@('scripts/Build-EngineProductionColdFixture.ps1','scripts/Test-EngineProductionColdFixture.ps1',
    'native/RizToolbox.EngineProductionFixture/Api.h','native/RizToolbox.EngineProductionFixture/Importer.cpp',
    'native/RizToolbox.EngineProductionFixture/Host.cpp','native/RizToolbox.WinHttpFixture/WinHttpFixtureApi.h',
    'native/RizToolbox.Doorstop/riz_engine_bootstrap.h',
    'native/RizToolbox.Doorstop/riz_winhttp.h',
    'docs/validation/foundation-engine-production-20261025/prepare-production-cold.py')
$sources=@($sourcePaths|ForEach-Object{[ordered]@{path=(Join-Path $repo $_);sha256=(Hash (Join-Path $repo $_))}})
New-Item -ItemType Directory -Path $output|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$sources|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'sources-before-build.json') -Encoding utf8
$contract=Join-Path $output 'production-cold-contract.json'
$header=Join-Path $output 'ProductionColdProfile.h'
& $Python $prepare --candidate $candidate --system-winhttp $SystemWinHttp --header $header --output $contract 2>&1|Tee-Object -FilePath (Join-Path $output 'prepare.log')
if($LASTEXITCODE -ne 0){throw 'Completed production candidate identity/MAP contract refused.'}
$contractData=Get-Content -LiteralPath $contract -Raw|ConvertFrom-Json
foreach($name in @('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){
    Copy-Item -LiteralPath (Join-Path $candidate $name) -Destination (Join-Path $output $name)
}
# Only the owned executable is affected. No CLR bootstrap or game content is
# requested, and this ini is part of the exact held child input inventory.
[IO.File]::WriteAllText((Join-Path $output 'doorstop_config.ini'),"[General]`nenabled=false`n",$utf8)
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
$common='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands+=@(
    "cl $common /LD /I`"$output`" /Fo`"$output\ProductionColdImporter.obj`" /Fd`"$output\ProductionColdImporter.compiler.pdb`" `"$fixture\Importer.cpp`" /link winhttp.lib /OUT:`"$output\RizProductionCold.Importer.dll`" /IMPLIB:`"$output\RizProductionCold.Importer.lib`" /PDB:`"$output\ProductionColdImporter.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /I`"$output`" /Fo`"$output\ProductionColdHost.obj`" /Fd`"$output\ProductionColdHost.compiler.pdb`" `"$fixture\Host.cpp`" /link `"$output\RizProductionCold.Importer.lib`" /OUT:`"$output\RizProductionCold.Host.exe`" /PDB:`"$output\ProductionColdHost.pdb`" $link",'if errorlevel 1 exit /b 1')
foreach($name in @('RizProductionCold.Host.exe','RizProductionCold.Importer.dll','winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){
    $path=Join-Path $output $name
    $commands+="dumpbin /nologo /headers /imports /exports /loadconfig /unwindinfo `"$path`" > `"$path.pe.txt`"";$commands+='if errorlevel 1 exit /b 1'
}
[IO.File]::WriteAllLines((Join-Path $output 'build-production-cold.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-production-cold.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'production-cold-build.log')
if($LASTEXITCODE -ne 0){throw "Owned production cold build failed: $LASTEXITCODE"}
& $Python $prepare --inspect-build $output --contract $contract --output (Join-Path $output 'production-cold-pe-proof.json') 2>&1|Tee-Object -FilePath (Join-Path $output 'inspect-build.log')
if($LASTEXITCODE -ne 0){throw 'Actual owned ordinary dependency/TLS/CFG image inspection failed.'}
foreach($row in $sources){if((Hash $row.path) -ne $row.sha256){throw "Declared cold build source changed: $($row.path)"}}
$candidateInputs=@($contractData.candidateManifest,$contractData.privateMap,
    @{path=$contractData.systemImage.path;sha256=$contractData.systemImage.sha256})
foreach($row in $contractData.images){$candidateInputs+=@{path=$row.path;sha256=$row.sha256}}
foreach($row in $candidateInputs){if((Hash $row.path) -ne $row.sha256){throw 'Fixed candidate input changed while building owned caller.'}}
$names=@('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll',
    'RizProductionCold.Importer.dll','RizProductionCold.Host.exe','doorstop_config.ini','ProductionColdProfile.h',
    'production-cold-contract.json','production-cold-pe-proof.json')
$files=@($names|ForEach-Object{[ordered]@{path=(Join-Path $output $_);sha256=(Hash (Join-Path $output $_))}})
[ordered]@{kind='owned-engine-production-cold-build';schemaVersion=2;files=$files;sources=$sources;candidateInputs=@($candidateInputs);
    candidateDirectory=$candidate;recordBytes=12448;snapshotBytes=42512;phaseProofBytes=14072;callsOffset=42256;callBytes=64;
    publicWinHttpExports=82;ordinaryDependencyEdges=4;earlySystemReferenceCalls=2;
    fixtureExecuted=$false;testProcessesStarted=0;gameExecuted=$false;installed=$false;productionAdmissionProved=$false}|
    ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $output 'production-cold-build-manifest.json') -Encoding utf8
Write-Output "Owned production cold fixture built; no child or game started: $output"
