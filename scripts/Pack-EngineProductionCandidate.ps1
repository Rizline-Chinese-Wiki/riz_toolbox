#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$BasePackageDirectory,[string]$BaseManifestPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'EngineProductionPackage.Common.ps1')
$repo=Split-Path -Parent $PSScriptRoot
if(-not $BasePackageDirectory){$BasePackageDirectory=Join-Path $repo 'artifacts/package'}
if(-not $BaseManifestPath){$BaseManifestPath=Join-Path $repo 'artifacts/package-manifest.json'}
$buildRoot=Assert-EngineOrdinaryPath $BuildDirectory -Directory
$baseRoot=Assert-EngineOrdinaryPath $BasePackageDirectory -Directory
$baseManifest=Assert-EngineOrdinaryPath $BaseManifestPath
$output=Assert-EngineOrdinaryPath $OutputDirectory -MayNotExist
Assert-EnginePackage (-not (Test-Path -LiteralPath $output)) 'use a fresh output; preserve prior failures and packages.'
$null=Assert-EngineOrdinaryPath (Split-Path -Parent $output) -Directory
foreach($inputRoot in @($buildRoot,$baseRoot)){Assert-EnginePackage (-not $output.StartsWith($inputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and -not $inputRoot.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $output -ine $inputRoot) 'input/output roots overlap.'}
$cursor=Split-Path -Parent $output
while($cursor){Assert-EnginePackage (-not (Test-Path -LiteralPath (Join-Path $cursor 'Rizline.exe'))) 'never create a candidate package in a game installation.';$parent=Split-Path -Parent $cursor;if($parent -eq $cursor){break};$cursor=$parent}
$base=Read-EnginePackageJson $baseManifest
Assert-EnginePackage ($base.schemaVersion -eq 2) 'original package schema differs.'
$basePaths=@(Get-EngineDefaultPackagePaths);Assert-EnginePackageTree $baseRoot $basePaths
$null=Assert-EngineFileRows $base.files $basePaths $baseRoot
$buildManifest=Read-EnginePackageJson (Get-EnginePackageFile $buildRoot 'production-build-manifest.json')
Assert-EnginePackage ($buildManifest.kind -ceq 'fixed-game-engine-production-candidate-build' -and $buildManifest.managedBuilt) 'a complete candidate build is required.'
Assert-EngineFalseFields $buildManifest @('productionAdmissionProved','gameExecuted','installed')
$nativePaths=@('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','doorstop.map','bootstrap.map','production-native-profile.json','production-managed-profile.json')
$null=Assert-EngineFileRows $buildManifest.files $nativePaths $buildRoot -Absolute
$managedManifest=Read-EnginePackageJson (Get-EnginePackageFile $buildRoot 'managed-build-manifest.json')
$managedNames=@('Sdk','Core','Runtime','Adapter','Hosting','Loader')|ForEach-Object{"managed/RizToolbox.$_.dll"}
$null=Assert-EngineFileRows $managedManifest.files $managedNames $buildRoot -Absolute
Assert-EngineDeclaredSources $buildManifest.sources $repo
Assert-EngineDeclaredSources $managedManifest.sources $repo

# No original artifacts/package byte is modified. Incomplete fresh outputs are
# deliberately retained and cannot pass the package reader's exact inventory.
New-Item -ItemType Directory -Path $output|Out-Null
$payloadRoot=Join-Path $output 'package';$evidenceRoot=Join-Path $output 'evidence'
New-Item -ItemType Directory -Path $payloadRoot,$evidenceRoot|Out-Null
function Copy-CandidateInput([string]$Source,[string]$Destination) {
    $sourcePath=Assert-EngineOrdinaryPath $Source;$expected=Get-EnginePackageHash $sourcePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force|Out-Null
    [IO.File]::Copy($sourcePath,$Destination,$false)
    Assert-EnginePackage ((Get-EnginePackageHash $Destination) -ceq $expected -and (Get-EnginePackageHash $sourcePath) -ceq $expected) "source changed while packing: $sourcePath"
}
$files=@()
foreach($relative in Get-EngineCandidatePackagePaths){
    $destination=Join-Path $payloadRoot $relative
    if($relative -ceq '.doorstop_version'){
        [IO.File]::WriteAllText($destination,'4.5.0-riz-engine-candidate.1',[Text.UTF8Encoding]::new($false))
    }else{
        $source=if($relative -cin @('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){Get-EnginePackageFile $buildRoot $relative}
        elseif($relative -cin @($managedNames|ForEach-Object{'BepInEx/plugins/RizToolbox/'+[IO.Path]::GetFileName($_)})){Get-EnginePackageFile $buildRoot ('managed/'+[IO.Path]::GetFileName($relative))}
        elseif($relative -clike 'BepInEx/plugins/RizToolbox/licenses/*'){Get-EnginePackageFile $buildRoot ('doorstop/'+[IO.Path]::GetFileName($relative))}
        else{Get-EnginePackageFile $baseRoot $relative}
        Copy-CandidateInput $source $destination
    }
    $files+=@{path=$relative;sha256=(Get-EnginePackageHash $destination)}
}
$evidence=@()
foreach($name in Get-EngineCandidateEvidencePaths){
    $source=if($name -ceq 'base-package-manifest.json'){$baseManifest}elseif($name -ceq 'doorstop-manifest.json'){Get-EnginePackageFile $buildRoot 'doorstop/doorstop-manifest.json'}else{Get-EnginePackageFile $buildRoot $name}
    $destination=Join-Path $evidenceRoot $name;Copy-CandidateInput $source $destination
    $evidence+=@{path=$name;sha256=(Get-EnginePackageHash $destination)}
}
[ordered]@{kind='fixed-game-engine-production-candidate-package';schemaVersion=1;version='0.3.0-engine-candidate';
    files=@($files);evidence=@($evidence);productionAdmissionGranted=$false;executionWindowProved=$false;gameExecuted=$false;installed=$false}|
    ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'candidate-package-manifest.json') -Encoding utf8
$validated=Get-EngineProductionCandidatePackage $output
Assert-EnginePackage ($validated.Relations.BuildRoot -ieq $buildRoot) 'build manifest belongs to another output.'
$null=Get-EngineCandidateRelations $evidenceRoot $validated.Payload -CheckLiveSources -Repository $repo
Write-Output "24-file candidate package verified (no production admission or installation): $output"
