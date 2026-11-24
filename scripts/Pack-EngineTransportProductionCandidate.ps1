#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$ManagedBuildManifest,
    [Parameter(Mandatory)][string]$OperationBuildDirectory,[Parameter(Mandatory)][string]$OperationProfileProof,
    [Parameter(Mandatory)][string]$OperationNativeProof,[Parameter(Mandatory)][string]$OperationManagedProof,
    [Parameter(Mandatory)][string]$TweenAdmissionProof,[Parameter(Mandatory)][string]$TweenSourceProof,
    [Parameter(Mandatory)][string]$TweenRetirementProof,[Parameter(Mandatory)][string]$TweenReferencesProof,
    [Parameter(Mandatory)][string]$OutputDirectory,[string]$BasePackageDirectory,[string]$BaseManifestPath,
    [string]$Python = '',[string]$OfflinePeDirectory)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1')
$repo=Split-Path -Parent $PSScriptRoot
if(-not $BasePackageDirectory){$BasePackageDirectory=Join-Path $repo 'artifacts/package'}
if(-not $BaseManifestPath){$BaseManifestPath=Join-Path $repo 'artifacts/package-manifest.json'}
$buildRoot=Assert-EngineOrdinaryPath $BuildDirectory -Directory
$managedPath=Assert-EngineOrdinaryPath $ManagedBuildManifest
$operationRoot=Assert-EngineOrdinaryPath $OperationBuildDirectory -Directory
$operationBuildPath=Assert-EngineOrdinaryPath (Join-Path $operationRoot 'native-manifest.json')
$operationBuild=Read-EnginePackageJson $operationBuildPath
$operationHelper=Assert-EngineOrdinaryPath (Join-Path $operationRoot 'RizToolbox.JudgeContext.dll')
$operationProfilePath=Assert-EngineOrdinaryPath $OperationProfileProof
$operationNativePath=Assert-EngineOrdinaryPath $OperationNativeProof
$operationResultPath=Assert-EngineOrdinaryPath (Join-Path (Split-Path -Parent $operationNativePath) 'fixture-result.json')
$operationManagedPath=Assert-EngineOrdinaryPath $OperationManagedProof
$admissionPath=Assert-EngineOrdinaryPath $TweenAdmissionProof
$admission=Read-EnginePackageJson $admissionPath
Assert-EnginePackage ($admission.kind -ceq 'typed-tween-admission-native-fixture-build' -and $admission.passed -eq $true -and
    $admission.compileExitCode -eq 0 -and $admission.testExitCode -eq 0 -and $admission.inputHashesUnchanged -eq $true -and
    $admission.nativeBuildManifest.sha256 -ceq (Get-EnginePackageHash $operationBuildPath) -and
    $admission.helper.sha256 -ceq (Get-EnginePackageHash $operationHelper)) 'a successful same-helper Tween admission execution proof is required.'
$admissionResult=Assert-EngineOrdinaryPath $admission.result.path
$admissionArchive=Assert-EngineOrdinaryPath $admission.sourceArchive.path
Assert-EnginePackage ((Get-EnginePackageHash $admissionResult) -ceq $admission.result.sha256 -and
    (Get-EnginePackageHash $admissionArchive) -ceq $admission.sourceArchive.sha256) 'Tween admission result or source archive changed.'
$sourcePath=Assert-EngineOrdinaryPath $TweenSourceProof
$source=Read-EnginePackageJson $sourcePath
Assert-EnginePackage ([IO.Path]::GetFileName($sourcePath) -ceq 'fixture-manifest.json' -and
    $source.schemaVersion -eq 1 -and $source.kind -ceq 'owned-native-tween-source-fixture-build' -and $source.passed -eq $true -and
    $source.compileExitCode -eq 0 -and $source.sourceHashesUnchanged -eq $true -and
    @($source.sourceFiles).Count -eq 14 -and @($source.modes).Count -eq 13) 'a successful actual owned Tween source fixture-manifest.json is required.'
Assert-EngineFalseFields $source @('productionHelperExecuted','gameExecuted','officialMetadataVerified')
$sourceResult=Assert-EngineOrdinaryPath $source.result.path
$sourceArchive=Assert-EngineOrdinaryPath $source.sourceArchive.path
Assert-EnginePackage ((Get-EnginePackageHash $sourceResult) -ceq $source.result.sha256 -and
    (Get-EnginePackageHash $sourceArchive) -ceq $source.sourceArchive.sha256 -and
    (Get-Item -LiteralPath $sourceArchive).Length -eq $source.sourceArchive.bytes) 'owned Tween source result or archived inputs changed.'
$retirementPath=Assert-EngineOrdinaryPath $TweenRetirementProof
$retirement=Read-EnginePackageJson $retirementPath
$retirementArchive=Assert-EngineOrdinaryPath $retirement.sourceArchive.path
Assert-EnginePackage ($retirement.kind -ceq 'owned-tween-retirement-native-build' -and $retirement.passed -eq $true -and
    $retirement.compileExitCode -eq 0 -and $retirement.sourceHashesUnchanged -eq $true -and
    (Get-EnginePackageHash $retirementArchive) -ceq $retirement.sourceArchive.sha256) 'actual retirement execution and frozen source proof required.'
$referencesPath=Assert-EngineOrdinaryPath $TweenReferencesProof
$references=Read-EnginePackageJson $referencesPath
$referencesResult=Assert-EngineOrdinaryPath $references.result.path
$referencesArchive=Assert-EngineOrdinaryPath $references.sourceArchive.path
Assert-EnginePackage ($references.kind -ceq 'owned-tween-source-references-native-build' -and $references.passed -eq $true -and
    $references.compileExitCode -eq 0 -and $references.sourceHashesUnchanged -eq $true -and
    (Get-EnginePackageHash $referencesResult) -ceq $references.result.sha256 -and
    (Get-EnginePackageHash $referencesArchive) -ceq $references.sourceArchive.sha256) 'actual nine-process reference execution and frozen source proof required.'
Assert-EnginePackage ($operationBuild.kind -ceq 'judge-context-operation-native-build' -and $operationBuild.passed -eq $true -and
    $operationBuild.buildExitCode -eq 0 -and $operationBuild.inputHashesUnchanged -eq $true -and
    $operationBuild.publicationVersion -eq 7 -and $operationBuild.operationCount -eq 26 -and $operationBuild.expectedWrites -eq 64 -and
    $operationBuild.tweenSourceCount -eq 22 -and @($operationBuild.expectedExports).Count -eq 88 -and
    $operationBuild.playbackAttestationVersion -eq 1 -and $operationBuild.playbackAttestationHeaderBytes -eq 120 -and
    $operationBuild.playbackAttestationRangeBytes -eq 184 -and $operationBuild.playbackAttestationOriginalCount -eq 28) 'a current V7 operation build with private playback attestation and twenty-two source sites is required.'
$operationArchive=Assert-EngineOrdinaryPath $operationBuild.sourceArchive.path
$operationExports=Assert-EngineOrdinaryPath $operationBuild.actualExportValidation.path
Assert-EnginePackage ((Get-EnginePackageHash $operationArchive) -ceq $operationBuild.sourceArchive.sha256 -and
    (Get-EnginePackageHash $operationExports) -ceq $operationBuild.actualExportValidation.sha256) 'operation source or actual PE export freeze changed.'
$baseRoot=Assert-EngineOrdinaryPath $BasePackageDirectory -Directory
$baseManifestPath=Assert-EngineOrdinaryPath $BaseManifestPath
$output=Assert-EngineOrdinaryPath $OutputDirectory -MayNotExist
Assert-EnginePackage (-not (Test-Path -LiteralPath $output)) 'preserve prior candidates; use a fresh output.'
$null=Assert-EngineOrdinaryPath (Split-Path -Parent $output) -Directory
foreach($inputRoot in @($buildRoot,$baseRoot,$operationRoot,(Split-Path -Parent $managedPath))){Assert-EnginePackage ($output -ine $inputRoot -and
    -not $output.StartsWith($inputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
    -not $inputRoot.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'input/output directories overlap.'}
$cursor=Split-Path -Parent $output
while($cursor){Assert-EnginePackage (-not (Test-Path -LiteralPath (Join-Path $cursor 'Rizline.exe'))) 'never pack into a game installation.';$next=Split-Path -Parent $cursor;if($next -eq $cursor){break};$cursor=$next}
$base=Read-EnginePackageJson $baseManifestPath
Assert-EnginePackage ($base.schemaVersion -in @(2,3)) 'a valid Schema 2 or Schema 3 base package manifest is required.'
$baseInheritedMap = @{}
if ($base.schemaVersion -eq 2) {
    $basePaths=@(Get-EngineTransportBasePackagePaths)
    Assert-EnginePackageTree $baseRoot $basePaths;$baseHashes=Assert-EngineFileRows $base.files $basePaths $baseRoot
    foreach($p in $basePaths){ $baseInheritedMap[$p] = Get-EnginePackageFile $baseRoot $p }
} else {
    $allBaseEntries = @($base.files) + @($(if($base.Contains('resources') -and $base.resources){$base.resources}else{@()}))
    $baseByPath = @{}
    foreach($row in $allBaseEntries){
        $fileOnDisk = Get-EnginePackageFile $baseRoot $row.path
        Assert-EnginePackage (-not $baseByPath.ContainsKey($row.path) -and (Get-EnginePackageHash $fileOnDisk) -ceq $row.sha256) "Duplicate or changed base package entry: $($row.path)"
        $baseByPath[$row.path] = $fileOnDisk
    }
    if ($base.Contains('packageKind')) {
        . (Join-Path $PSScriptRoot 'PackageDistribution.Common.ps1')
        Assert-RuntimeDistribution $baseRoot $base $base.packageKind
    }
    $inheritedPaths = Get-EngineTransportSchema3InheritedPaths $baseByPath $base
    foreach($target in $inheritedPaths.Keys){
        $baseInheritedMap[$target] = $baseByPath[$inheritedPaths[$target]]
    }
}
$nativePath=Join-Path $buildRoot 'production-native-build.json';$native=Read-EnginePackageJson $nativePath
$offlineRoot = if ($OfflinePeDirectory) { Assert-EngineOrdinaryPath $OfflinePeDirectory -Directory }
$fixedImages = @('Rizline.exe','UnityPlayer.dll')
$nativeInputPaths = @(
    foreach ($row in $native.inputs) {
        $name = [IO.Path]::GetFileName($row.path)
        if ($offlineRoot -and $name -cin $fixedImages) { Join-Path $offlineRoot $name } else { $row.path }
    }
    if ($offlineRoot) { Join-Path $offlineRoot 'GameAssembly.dll' }
)
$managed=Read-EnginePackageJson $managedPath
Assert-EnginePackage ($managed.kind -ceq 'engine-transport-production-managed-candidate-build' -and $managed.passed -eq $true -and $managed.buildExitCode -eq 0) 'actual completed V5 managed build required.'
$snapshot=Assert-EngineOrdinaryPath $managed.sourceSnapshot.manifestPath
Assert-EnginePackage ((Get-EnginePackageHash $snapshot) -ceq $managed.sourceSnapshot.sha256) 'managed source snapshot changed.'
$snapshotProof=Read-EnginePackageJson $snapshot
$snapshotArchive=Assert-EngineOrdinaryPath $snapshotProof.sourceArchive.path
Assert-EnginePackage ((Get-EnginePackageHash $snapshotArchive) -ceq $snapshotProof.sourceArchive.sha256) 'compiled managed source archive changed.'
$operationManaged=Read-EnginePackageJson $operationManagedPath
Assert-EnginePackage ($operationManaged.kind -ceq 'mapped-image-managed-preinit-adoption-summary' -and $operationManaged.passed -eq $true -and
    $operationManaged.publicationVersion -eq 7 -and $operationManaged.operationCount -eq 26 -and $operationManaged.expectedWrites -eq 64 -and
    $operationManaged.helperSha256 -ceq (Get-EnginePackageHash $operationHelper) -and
    $operationManaged.sourceSnapshot.sha256 -ceq $managed.sourceSnapshot.sha256) 'actual current native/CLR adoption must use this compiled managed snapshot and helper.'
$doorstopRows=@($native.inputs|Where-Object{[IO.Path]::GetFileName($_.path) -ceq 'transport-doorstop-build.json'})
Assert-EnginePackage ($doorstopRows.Count -eq 1) 'unique production Doorstop build evidence required.'
$doorstopPath=Assert-EngineOrdinaryPath $doorstopRows[0].path
Assert-EnginePackage ((Get-EnginePackageHash $doorstopPath) -ceq $doorstopRows[0].sha256) 'Doorstop build freeze changed.'
$locks=[Collections.Generic.List[IDisposable]]::new()
try{
    $lockPaths=@(@($native.files.path)+$nativeInputPaths+@($managed.files.path)+@($baseInheritedMap.Values)+
        @($operationBuild.files.path)+
        @($nativePath,$managedPath,$snapshot,$snapshotArchive,$baseManifestPath,$doorstopPath,$operationBuildPath,$operationHelper,
            $operationArchive,$operationExports,$operationProfilePath,$operationNativePath,$operationResultPath,$operationManagedPath,
            $admissionPath,$admissionResult,$admissionArchive,$sourcePath,$sourceResult,$sourceArchive,
            $retirementPath,$retirementArchive,$referencesPath,$referencesResult,$referencesArchive)|Sort-Object -Unique)
    foreach($path in $lockPaths){$locks.Add([IO.File]::Open((Assert-EngineOrdinaryPath $path),'Open','Read','Read'))}
    foreach($item in $operationBuild.files){
        Assert-EnginePackage ((Get-EnginePackageHash $item.path) -ceq $item.sha256) 'operation compiled output changed before packing.'
    }
    Assert-EnginePackage ((Get-EnginePackageHash $operationArchive) -ceq $operationBuild.sourceArchive.sha256 -and
        (Get-EnginePackageHash $admissionArchive) -ceq $admission.sourceArchive.sha256 -and
        (Get-EnginePackageHash $sourceArchive) -ceq $source.sourceArchive.sha256) 'a successful archived source freeze changed before locking.'
    $compiledInputs=Read-EngineTransportArchivedSources $operationArchive $operationBuild.sources
    $admissionInputs=Read-EngineTransportArchivedSources $admissionArchive $admission.sourceFiles
    $sourceInputs=Read-EngineTransportArchivedSources $sourceArchive $source.sourceFiles
    foreach($name in $admissionInputs.Keys|Where-Object{$_ -clike 'native/*'}){
        Assert-EnginePackage ($compiledInputs.ContainsKey($name) -and
            [Convert]::ToBase64String($compiledInputs[$name]) -ceq [Convert]::ToBase64String($admissionInputs[$name])) 'the actual admission fixture used different archived production sources.'
    }
    $sourceCore=@('TweenSourceContext.cpp','TweenSourceContext.h','TweenSourceProfile.h','TweenSourceEntry.asm','MidContext.h','TweenAdmission.h',
        'TweenSourceContext.References.h','TweenSourceReferences.h','TweenSourceReferences.cpp','TweenRetirementContext.h','TweenRetirementContext.cpp')|
        ForEach-Object{"native/RizToolbox.JudgeContext/$_"}
    $sourceNames=@($sourceCore)+@('native/RizToolbox.JudgeContext/fixture/TweenSourceFixture.cpp',
        'native/RizToolbox.JudgeContext/fixture/TweenSourceFixtureCaller.asm','scripts/Test-TweenSourcesNative.ps1')
    Assert-EnginePackage ($sourceCore.Count -eq 11 -and $sourceInputs.Count -eq 14 -and
        @($sourceInputs.Keys|Where-Object{$_ -cnotin $sourceNames}).Count -eq 0) 'the owned source archive must contain its exact fourteen inputs and eleven production core files.'
    foreach($name in $sourceCore){
        Assert-EnginePackage ($compiledInputs.ContainsKey($name) -and
            [Convert]::ToBase64String($compiledInputs[$name]) -ceq [Convert]::ToBase64String($sourceInputs[$name])) 'the owned source fixture used different archived production core bytes.'
    }
    foreach($proof in @(@{Archive=$retirementArchive;Rows=$retirement.sources},@{Archive=$referencesArchive;Rows=$references.sourceFiles})){
        $proofInputs=Read-EngineTransportArchivedSources $proof.Archive $proof.Rows
        foreach($name in $proofInputs.Keys|Where-Object{$_ -clike 'native/*' -and $_ -cnotlike '*/fixture/*'}){
            Assert-EnginePackage ($compiledInputs.ContainsKey($name) -and
                [Convert]::ToBase64String($compiledInputs[$name]) -ceq [Convert]::ToBase64String($proofInputs[$name])) 'the retirement/reference fixture used different actual compiled production core bytes.'
        }
    }
    $validationArgs = @('-B',(Join-Path $PSScriptRoot 'verify-engine-transport-production-package.py'),'--build-directory',$buildRoot,'--print-json')
    if ($offlineRoot) { $validationArgs += @('--game-directory',$offlineRoot) }
    $finalLines=@(& $Python @validationArgs)
    Assert-EnginePackage ($LASTEXITCODE -eq 0) 'independent final native PE validation failed.'
    $final=($finalLines -join "`n")|ConvertFrom-Json -AsHashtable
    Assert-EnginePackage ($final.passed -eq $true) 'independent native validation did not pass.'
    $null=New-Item -ItemType Directory -Path $output
    $payloadRoot=Join-Path $output 'package';$evidenceRoot=Join-Path $output 'evidence'
    $null=New-Item -ItemType Directory -Path $payloadRoot,$evidenceRoot
    function Copy-TransportFile([string]$Source,[string]$Destination){
        $sourcePath=Assert-EngineOrdinaryPath $Source;$hash=Get-EnginePackageHash $sourcePath
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force
        [IO.File]::Copy($sourcePath,$Destination,$false)
        Assert-EnginePackage ((Get-EnginePackageHash $sourcePath) -ceq $hash -and (Get-EnginePackageHash $Destination) -ceq $hash) 'input changed during candidate packing.'
    }
    $managedFiles=@{};foreach($item in $managed.files){$name=[IO.Path]::GetFileName($item.path)
        Assert-EnginePackage (-not $managedFiles.ContainsKey($name) -and (Get-EnginePackageHash $item.path) -ceq $item.sha256) 'managed output changed or duplicated.';$managedFiles[$name]=$item.path}
    $payload=@();foreach($relative in Get-EngineTransportPackagePaths $base){$destination=Join-Path $payloadRoot $relative
        if($relative -ceq '.doorstop_version'){[IO.File]::WriteAllText($destination,'4.5.0-riz-engine-transport-candidate.5',[Text.UTF8Encoding]::new($false))}
        else{
            $name=[IO.Path]::GetFileName($relative)
            $source=if($relative -cin @('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){Get-EnginePackageFile $buildRoot $relative}
            elseif($relative.StartsWith('BepInEx/plugins/RizToolbox/',[StringComparison]::Ordinal) -and $managedFiles.ContainsKey($name)){$managedFiles[$name]}
            elseif($relative -ceq 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'){$operationHelper}
            elseif($relative -clike 'BepInEx/plugins/RizToolbox/licenses/*'){Get-EnginePackageFile $buildRoot $name}
            else{$baseInheritedMap[$relative]}
            Copy-TransportFile $source $destination
        }
        $payload+=@{path=$relative;sha256=Get-EnginePackageHash $destination;bytes=(Get-Item -LiteralPath $destination).Length}
    }
    $evidence=@();foreach($name in Get-EngineTransportEvidencePaths){$destination=Join-Path $evidenceRoot $name
        if($name -ceq 'final-native-validation.json'){[IO.File]::WriteAllText($destination,($final|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))}
        else{$source=switch -CaseSensitive ($name){
            'base-package-manifest.json' {$baseManifestPath}
            'transport-managed-build.json' {$managedPath}
            'managed-source-snapshot.json' {$snapshot}
            'transport-doorstop-build.json' {$doorstopPath}
            'operation-native-build.json' {$operationBuildPath}
            'operation-native-sources.zip' {$operationArchive}
            'operation-export-validation.json' {$operationExports}
            'operation-profile-verification.json' {$operationProfilePath}
            'operation-native-proof.json' {$operationNativePath}
            'operation-native-result.json' {$operationResultPath}
            'operation-managed-proof.json' {$operationManagedPath}
            'tween-admission-native-proof.json' {$admissionPath}
            'tween-admission-native-result.json' {$admissionResult}
            'tween-admission-native-sources.zip' {$admissionArchive}
            'tween-source-native-proof.json' {$sourcePath}
            'tween-source-native-result.json' {$sourceResult}
            'tween-source-native-sources.zip' {$sourceArchive}
            'tween-retirement-native-proof.json' {$retirementPath}
            'tween-retirement-native-sources.zip' {$retirementArchive}
            'tween-references-native-proof.json' {$referencesPath}
            'tween-references-native-result.json' {$referencesResult}
            'tween-references-native-sources.zip' {$referencesArchive}
            'managed-source-snapshot.zip' {$snapshotArchive}
            default {Get-EnginePackageFile $buildRoot $name}
        };Copy-TransportFile $source $destination}
        $evidence+=@{path=$name;sha256=Get-EnginePackageHash $destination;bytes=(Get-Item -LiteralPath $destination).Length}
    }
    $admitted = ($final.productionAdmissionGranted -eq $true)
    $manifest=[ordered]@{schemaVersion=1;kind='engine-transport-production-candidate-package';payloadScope='full';publicationVersion=5;nativeProfile=2;
        operationPublicationVersion=7;operationPublicationBytes=2528;operationCount=26;operationExpectedWrites=64;tweenSourceCount=22;
        singleEventCapabilityVersion=1;singleEventSectionBytes=416;singleEventSpanCount=7;singleEventExpectedWrites=7;helperExportCount=88;
        playbackAttestationVersion=1;playbackAttestationHeaderBytes=120;playbackAttestationRangeBytes=184;playbackAttestationOriginalCount=28;
        files=$payload;evidence=$evidence;productionAdmissionGranted=$admitted;executionWindowProved=$admitted;gameExecuted=$false;installed=$false}
    [IO.File]::WriteAllText((Join-Path $output 'transport-candidate-package-manifest.json'),($manifest|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $null=Get-EngineTransportProductionCandidatePackage $output
    $game=[IO.Path]::GetDirectoryName($final.images[2].path)
    $null=Test-EngineTransportPackagePe $output $game $Python
}finally{foreach($lock in $locks){$lock.Dispose()}}
Write-Output "Complete $($payload.Count)-file transport V5 / operation V7 candidate with $($evidence.Count) evidence files verified (productionAdmissionGranted=$admitted; not installed): $output"
