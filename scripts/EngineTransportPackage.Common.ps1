#requires -Version 7.2
Set-StrictMode -Version Latest
# Reuse only ordinary-path/tree/hash utilities. Current package relations are
# validated below against transport V5 and the exclusive operation V7 contract.
. (Join-Path $PSScriptRoot 'EngineProductionPackage.Common.ps1')

function Get-EngineTransportBasePackagePaths {
    return @(Get-EngineDefaultPackagePaths)
}
function Get-EngineTransportPackagePaths($Base) {
    $fixed = @(Get-EngineCandidatePackagePaths)
    if (!$Base -or (!$Base.Contains('packageKind') -and !$Base.Contains('basicPlugins'))) { return $fixed }
    Assert-EnginePackage ($Base.schemaVersion -eq 3 -and $Base.packageKind -cin @('Runtime','Author','Validation') -and
        $Base.basicPlugins -is [array] -and $Base.developmentAssetsInstalled -is [bool] -and !$Base.developmentAssetsInstalled -and
        $Base.proofEnabledByPackage -is [bool] -and !$Base.proofEnabledByPackage) 'invalid distribution plugin selection.'
    $modPaths = @(foreach ($id in $Base.basicPlugins) {
        Assert-EnginePackage ($id -is [string] -and $id -cin @('basic.song-scores','basic.theme','basic.quick-panel')) 'unknown ordinary plugin.'
        "BepInEx/RizToolbox/mods/$id/plugin.json"
        "BepInEx/RizToolbox/mods/$id/RizToolbox.BasicPlugins.dll"
    })
    $declared = @($Base.files | ForEach-Object { $_.path } | Where-Object { $_ -like 'BepInEx/RizToolbox/mods/*' })
    Assert-EnginePackage (@($modPaths | Sort-Object -Unique).Count -eq $modPaths.Count -and
        $declared.Count -eq $modPaths.Count -and !(Compare-Object $declared $modPaths -CaseSensitive)) 'runtime plugin files differ from the selection.'
    return @($fixed | Where-Object { $_ -cnotlike 'BepInEx/RizToolbox/mods/*' }) + $modPaths
}
function Get-EngineTransportSchema3InheritedPaths($BaseRows, $Base) {
    $Base ??= @{}
    $modsRoot = 'BepInEx/RizToolbox/mods/'
    $inherited = @(
        'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json',
        'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll',
        'BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll'
    ) + @(Get-EngineTransportPackagePaths $Base | Where-Object { $_.StartsWith($modsRoot, [StringComparison]::Ordinal) })
    $paths = @{}
    foreach ($target in $inherited) {
        $options = @($target)
        if (!$Base.Contains('packageKind') -and $target.StartsWith($modsRoot, [StringComparison]::Ordinal)) {
            $suffix = $target.Substring($modsRoot.Length)
            $options += "author/examples/packages/$suffix", "validation/proof/packages/$suffix"
        }
        $source = $options | Where-Object { $BaseRows.ContainsKey($_) } | Select-Object -First 1
        Assert-EnginePackage ($null -ne $source) "Schema 3 base package is missing $target."
        $paths[$target] = $source
    }
    return $paths
}
function Get-EngineTransportEvidencePaths {
    return @('production-native-build.json','native-profile.json','final-native-validation.json',
        'NativeEngineTransportProfile.Generated.cs','EngineTransportProfile.Generated.h','production-native-sources.zip',
        'bootstrap.map','build-production-transport.cmd','transport-doorstop-build.json','base-package-manifest.json',
        'transport-managed-build.json','managed-source-snapshot.json','managed-source-snapshot.zip',
        'operation-native-build.json','operation-native-sources.zip','operation-export-validation.json',
        'operation-profile-verification.json','operation-native-proof.json','operation-native-result.json','operation-managed-proof.json',
        'tween-admission-native-proof.json','tween-admission-native-result.json','tween-admission-native-sources.zip',
        'tween-source-native-proof.json','tween-source-native-result.json','tween-source-native-sources.zip',
        'tween-retirement-native-proof.json','tween-retirement-native-sources.zip',
        'tween-references-native-proof.json','tween-references-native-result.json','tween-references-native-sources.zip')
}
function Read-EngineTransportArchivedSources([string]$ArchivePath,$Rows) {
    # Compilation evidence is the successful build's frozen archive. Never
    # reopen its mutable producer worktree while packing or after relocation.
    $declared=@{};foreach($row in $Rows){
        $name=$row.relativePath
        Assert-EnginePackage ($name -is [string] -and $name -cmatch '^[A-Za-z0-9_.\-/]+$' -and
            -not [IO.Path]::IsPathRooted($name) -and -not $name.Contains('\') -and
            @($name.Split('/')|Where-Object{$_ -in @('','.','..')}).Count -eq 0 -and
            -not $declared.ContainsKey($name) -and $row.sha256 -cmatch '^[0-9a-f]{64}$' -and $row.bytes -gt 0) 'invalid archived compilation input.'
        $declared[$name]=$row
    }
    Assert-EnginePackage ($declared.Count -gt 0) 'archived compilation inputs are missing.'
    $archive=[IO.Compression.ZipFile]::OpenRead((Assert-EngineOrdinaryPath $ArchivePath));$values=@{}
    try{
        Assert-EnginePackage ($archive.Entries.Count -eq $declared.Count) 'compiled source archive entry count differs.'
        foreach($entry in $archive.Entries){$name=$entry.FullName
            Assert-EnginePackage ($declared.ContainsKey($name) -and -not $values.ContainsKey($name) -and
                (($entry.ExternalAttributes -shr 16) -band 0xf000) -ne 0xa000 -and
                $entry.Length -eq $declared[$name].bytes -and $entry.Length -le 16777216) 'unexpected/linked/duplicate archived source.'
            $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
            try{$stream.CopyTo($memory);$bytes=$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
            $sha=[Security.Cryptography.SHA256]::Create()
            try{$hash=[Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant()}finally{$sha.Dispose()}
            Assert-EnginePackage ($hash -ceq $declared[$name].sha256 -and $bytes.Length -eq $declared[$name].bytes) 'frozen compiled source byte differs.'
            $values[$name]=$bytes
        }
    }finally{$archive.Dispose()}
    return $values
}
function Assert-EngineTransportRows($Rows,[string[]]$Expected,[string]$Root) {
    $hashes=Assert-EngineFileRows $Rows $Expected $Root
    foreach($item in $Rows){
        Assert-EnginePackage ($item.Contains('bytes') -and ($item.bytes -is [long] -or $item.bytes -is [int]) -and $item.bytes -gt 0) 'positive integer file length is missing.'
        Assert-EnginePackage ((Get-Item -LiteralPath (Get-EnginePackageFile $Root $item.path)).Length -eq $item.bytes) 'file length changed.'
    }
    return $hashes
}
function Get-EngineTransportProductionCandidatePackage([string]$CandidateDirectory) {
    $root=Assert-EngineOrdinaryPath $CandidateDirectory -Directory
    $manifest=Read-EnginePackageJson (Get-EnginePackageFile $root 'transport-candidate-package-manifest.json')
    Assert-EnginePackage ($manifest.kind -ceq 'engine-transport-production-candidate-package' -and $manifest.schemaVersion -eq 1 -and
        $manifest.payloadScope -ceq 'full' -and $manifest.publicationVersion -eq 5 -and $manifest.nativeProfile -eq 2) 'a complete V5 production candidate package is required.'
    Assert-EngineFalseFields $manifest @('gameExecuted','installed')
    $admitted = [bool]$manifest.productionAdmissionGranted
    Assert-EnginePackage ($manifest.executionWindowProved -eq $admitted) 'package executionWindowProved must match productionAdmissionGranted.'
    Assert-EnginePackage ($manifest.operationPublicationVersion -eq 7 -and $manifest.operationPublicationBytes -eq 2528 -and $manifest.operationCount -eq 26 -and
        $manifest.operationExpectedWrites -eq 64 -and $manifest.tweenSourceCount -eq 22 -and $manifest.helperExportCount -eq 88 -and
        $manifest.playbackAttestationVersion -eq 1 -and $manifest.playbackAttestationHeaderBytes -eq 120 -and
        $manifest.playbackAttestationRangeBytes -eq 184 -and $manifest.playbackAttestationOriginalCount -eq 28 -and
        $manifest.singleEventCapabilityVersion -eq 1 -and $manifest.singleEventSectionBytes -eq 416 -and
        $manifest.singleEventSpanCount -eq 7 -and $manifest.singleEventExpectedWrites -eq 7) 'the current package must carry operation V7 and the complete Single publication.'
    $payloadRoot=Join-Path $root 'package';$evidenceRoot=Join-Path $root 'evidence'
    $evidencePaths=@(Get-EngineTransportEvidencePaths)
    $evidence=Assert-EngineTransportRows $manifest.evidence $evidencePaths $evidenceRoot
    $base=Read-EnginePackageJson (Join-Path $evidenceRoot 'base-package-manifest.json')
    $paths=@(Get-EngineTransportPackagePaths $base)
    $payload=Assert-EngineTransportRows $manifest.files $paths $payloadRoot
    Assert-EnginePackageTree $root (@('transport-candidate-package-manifest.json')+@($paths|ForEach-Object{"package/$_"})+@($evidencePaths|ForEach-Object{"evidence/$_"}))
    Assert-EnginePackage ([IO.File]::ReadAllText((Join-Path $payloadRoot '.doorstop_version')) -ceq '4.5.0-riz-engine-transport-candidate.5') 'V5 candidate marker differs.'
    $native=Read-EnginePackageJson (Join-Path $evidenceRoot 'production-native-build.json')
    $facts=Read-EnginePackageJson (Join-Path $evidenceRoot 'native-profile.json')
    $final=Read-EnginePackageJson (Join-Path $evidenceRoot 'final-native-validation.json')
    $managed=Read-EnginePackageJson (Join-Path $evidenceRoot 'transport-managed-build.json')
    $doorstop=Read-EnginePackageJson (Join-Path $evidenceRoot 'transport-doorstop-build.json')
    Assert-EnginePackage ($native.kind -ceq 'engine-transport-production-native-candidate-build' -and $native.passed -eq $true -and
        $native.buildExitCode -eq 0 -and $native.sourceHashesUnchanged -eq $true -and $native.gameFilesUnchanged -eq $true) 'actual successful fixed-production native build required.'
    Assert-EngineFalseFields $native @('fixtureExecuted','gameExecuted','installed','packaged')
    Assert-EnginePackage ($native.productionAdmissionGranted -eq $admitted -and $native.productionAdmissionVerified -eq $admitted) 'native build admission state differs from package.'
    Assert-EnginePackage ($facts.kind -ceq 'engine-transport-native-compile-facts' -and $facts.profile -ceq 'production' -and
        $facts.nativeProfile -eq 2 -and @($facts.images).Count -eq 5 -and @($facts.edges).Count -eq 44) 'owned or legacy compile facts refused.'
    Assert-EngineFalseFields $facts @('nativePublicationReadyProved','kernelConfigured','sourcePatched','targetImagesLoaded','gameExecuted','installed')
    Assert-EnginePackage ($facts.productionAdmissionGranted -eq $admitted -and $facts.executionWindowProved -eq $admitted) 'compile facts admission state differs from package.'
    Assert-EnginePackage ($final.kind -ceq 'engine-transport-production-final-native-validation' -and $final.passed -eq $true -and
        $final.publicationVersion -eq 5 -and $final.publicationBytes -eq 91376 -and $final.nativeProfile -eq 2 -and
        $final.finalGraphComplete -eq $true -and $final.sourceGateClosed -eq (-not $admitted) -and @($final.images).Count -eq 5 -and @($final.edges).Count -eq 44) 'complete final five-image/44-edge V5 validation is required.'
    Assert-EngineFalseFields $final @('targetImagesLoaded','nativePublicationReadyProved','gameExecuted','installed')
    Assert-EnginePackage ($final.productionAdmissionGranted -eq $admitted -and $final.productionAdmissionVerified -eq $admitted) 'final native validation admission state differs from package.'
    $expectedModules=@('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','UnityPlayer.dll','Rizline.exe','winhttp.dll')
    $pins=@('','1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a',
        '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1','f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e','')
    for($i=0;$i -lt 5;++$i){$image=$final.images[$i]
        Assert-EnginePackage ($image.index -eq $i -and $image.module -ceq $expectedModules[$i] -and $image.sha256 -cmatch '^[0-9a-f]{64}$') 'final native role order differs.'
        if($pins[$i]){Assert-EnginePackage ($image.sha256 -ceq $pins[$i]) 'fixed game/Kernel fingerprint differs.'}
        if($i -in @(0,1,4)){Assert-EnginePackage ($payload[$image.module] -ceq $image.sha256) 'payload PE differs from final disk validation.'}
    }
    $nativeOutputs=@{};foreach($item in $native.files){$name=[IO.Path]::GetFileName($item.path)
        Assert-EnginePackage (-not $nativeOutputs.ContainsKey($name) -and $item.sha256 -cmatch '^[0-9a-f]{64}$') 'duplicate native build artifact.';$nativeOutputs[$name]=$item.sha256}
    foreach($name in @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','winhttp.dll')){
        Assert-EnginePackage ($nativeOutputs[$name] -ceq $payload[$name]) 'final native payload provenance differs.'}
    foreach($name in @('native-profile.json','NativeEngineTransportProfile.Generated.cs','EngineTransportProfile.Generated.h','production-native-sources.zip','bootstrap.map','build-production-transport.cmd')){
        Assert-EnginePackage ($nativeOutputs[$name] -ceq $evidence[$name]) 'final native evidence provenance differs.'}
    Assert-EnginePackage ($final.buildManifest.sha256 -ceq $evidence['production-native-build.json'] -and
        $final.nativeCompileFacts.sha256 -ceq $evidence['native-profile.json'] -and
        $final.generatedSource.sha256 -ceq $evidence['NativeEngineTransportProfile.Generated.cs'] -and
        $native.managedProfile.sha256 -ceq $evidence['NativeEngineTransportProfile.Generated.cs'] -and
        $facts.generatedHeader.sha256 -ceq $evidence['EngineTransportProfile.Generated.h']) 'generated native/managed source chain differs.'
    Assert-EnginePackage ($managed.schemaVersion -eq 1 -and $managed.kind -ceq 'engine-transport-production-managed-candidate-build' -and
        $managed.passed -eq $true -and $managed.buildExitCode -eq 0 -and $managed.sourceHashesUnchanged -eq $true) 'a real complete V5 managed compilation is required.'
    Assert-EngineFalseFields $managed @('gameExecuted','installed')
    Assert-EnginePackage ($managed.productionAdmissionGranted -eq $admitted -and $managed.executionWindowProved -eq $admitted) 'managed build admission state differs from package.'
    Assert-EnginePackage ($managed.generatedSource.sha256 -ceq $evidence['NativeEngineTransportProfile.Generated.cs'] -and
        $managed.nativeBuild.sha256 -ceq $evidence['production-native-build.json'] -and
        $managed.sourceSnapshot.sha256 -ceq $evidence['managed-source-snapshot.json']) 'managed build used a different native profile or source snapshot.'
    Assert-EnginePackage ([IO.Path]::IsPathFullyQualified($managed.generatedSource.path) -and
        [IO.Path]::GetFileName($managed.generatedSource.path) -ceq 'NativeEngineTransportProfile.Generated.cs' -and
        [IO.Path]::IsPathFullyQualified($managed.nativeBuild.path) -and [IO.Path]::GetFileName($managed.nativeBuild.path) -ceq 'production-native-build.json') 'managed build source-role paths differ.'
    $managedNames=@('Sdk','Core','Runtime','Adapter','Hosting','Loader')|ForEach-Object{"RizToolbox.$_.dll"};$seen=@{}
    Assert-EnginePackage (@($managed.files).Count -eq 6) 'exactly six compiled managed payloads required.'
    foreach($item in $managed.files){$name=[IO.Path]::GetFileName($item.path)
        Assert-EnginePackage ([IO.Path]::IsPathFullyQualified($item.path) -and $name -cin $managedNames -and -not $seen.ContainsKey($name) -and
            $item.sha256 -ceq $payload["BepInEx/plugins/RizToolbox/$name"] -and
            $item.bytes -eq (Get-Item -LiteralPath (Join-Path $payloadRoot "BepInEx/plugins/RizToolbox/$name")).Length) 'managed payload provenance differs.';$seen[$name]=$true}
    Assert-EnginePackage ($doorstop.kind -ceq 'engine-transport-doorstop-build' -and $doorstop.passed -eq $true -and
        $doorstop.owned -eq $false -and $doorstop.sourceHashesUnchanged -eq $true) 'actual production transport Doorstop required.'
    Assert-EngineFalseFields $doorstop @('installed','gameExecuted')
    Assert-EnginePackage ($doorstop.productionAdmissionVerified -eq $admitted) 'Doorstop productionAdmissionVerified state differs from package.'
    Assert-EnginePackage ($final.correspondingSource.sha256 -ceq $payload['BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip'] -and
        $final.license.sha256 -ceq $payload['BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt']) 'actual corresponding source/license differs.'
    Assert-EnginePackage ($base.schemaVersion -in @(2,3)) 'a valid Schema 2 or Schema 3 base package manifest is required.'
    if ($base.schemaVersion -eq 2) {
        $basePaths=@(Get-EngineTransportBasePackagePaths);$baseRows=@{}
        Assert-EnginePackage (@($base.files).Count -eq $basePaths.Count) 'the complete base freeze is required.'
        foreach($item in $base.files){Assert-EnginePackage ($item.path -cin $basePaths -and -not $baseRows.ContainsKey($item.path) -and $item.sha256 -cmatch '^[0-9a-f]{64}$') 'invalid base package freeze.';$baseRows[$item.path]=$item.sha256}
        $replaced=@('winhttp.dll','.doorstop_version','BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll',
            'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll',
            'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip','BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt')+
            @($managedNames|ForEach-Object{"BepInEx/plugins/RizToolbox/$_"})
        foreach($name in $basePaths|Where-Object{$_ -cnotin $replaced}){Assert-EnginePackage ($payload[$name] -ceq $baseRows[$name]) 'an inherited base package byte differs.'}
    } else {
        $baseRows=@{}
        foreach($item in @($base.files) + @($(if($base.Contains('resources') -and $base.resources){$base.resources}else{@()}))){
            Assert-EnginePackage ($item.sha256 -cmatch '^[0-9a-f]{64}$' -and -not $baseRows.ContainsKey($item.path)) 'invalid or duplicate Schema 3 base row.'
            $baseRows[$item.path]=$item.sha256
        }
        $inheritedPaths = Get-EngineTransportSchema3InheritedPaths $baseRows $base
        foreach($name in $inheritedPaths.Keys){
            Assert-EnginePackage ($payload[$name] -ceq $baseRows[$inheritedPaths[$name]]) "an inherited Schema 3 base package byte differs: $name"
        }
    }
    $operation=Read-EnginePackageJson (Join-Path $evidenceRoot 'operation-native-build.json')
    Assert-EnginePackage ($operation.kind -ceq 'judge-context-operation-native-build' -and $operation.passed -eq $true -and
        $operation.buildExitCode -eq 0 -and $operation.inputHashesUnchanged -eq $true -and $operation.publicationVersion -eq 7 -and
        $operation.operationCount -eq 26 -and $operation.expectedWrites -eq 64 -and $operation.tweenSourceCount -eq 22 -and
        @($operation.expectedExports).Count -eq 88 -and $operation.playbackAttestationVersion -eq 1 -and
        $operation.playbackAttestationHeaderBytes -eq 120 -and $operation.playbackAttestationRangeBytes -eq 184 -and
        $operation.playbackAttestationOriginalCount -eq 28) 'current operation helper and private playback attestation provenance are required.'
    $helperRows=@($operation.files|Where-Object{[IO.Path]::GetFileName($_.path) -ceq 'RizToolbox.JudgeContext.dll'})
    Assert-EnginePackage ($helperRows.Count -eq 1 -and $helperRows[0].sha256 -ceq $payload['BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'] -and
        $operation.sourceArchive.sha256 -ceq $evidence['operation-native-sources.zip'] -and
        $operation.actualExportValidation.sha256 -ceq $evidence['operation-export-validation.json']) 'current helper/source/export provenance differs.'
    $adoption=Read-EnginePackageJson (Join-Path $evidenceRoot 'operation-managed-proof.json')
    Assert-EnginePackage ($adoption.passed -eq $true -and $adoption.publicationVersion -eq 7 -and $adoption.operationCount -eq 26 -and
        $adoption.expectedWrites -eq 64 -and $adoption.helperSha256 -ceq $helperRows[0].sha256 -and
        $adoption.sourceSnapshot.sha256 -ceq $evidence['managed-source-snapshot.json']) 'actual V7 adoption differs from the payload helper or managed source snapshot.'
    $admission=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-admission-native-proof.json')
    Assert-EnginePackage ($admission.kind -ceq 'typed-tween-admission-native-fixture-build' -and $admission.passed -eq $true -and
        $admission.compileExitCode -eq 0 -and $admission.testExitCode -eq 0 -and $admission.inputHashesUnchanged -eq $true -and
        $admission.sameProductionGateAndDispatchSources -eq $true -and
        $admission.nativeBuildManifest.sha256 -ceq $evidence['operation-native-build.json'] -and
        $admission.helper.sha256 -ceq $helperRows[0].sha256 -and
        $admission.result.sha256 -ceq $evidence['tween-admission-native-result.json'] -and
        $admission.sourceArchive.sha256 -ceq $evidence['tween-admission-native-sources.zip']) 'actual Tween gate execution/source evidence differs from this helper.'
    Assert-EngineFalseFields $admission @('productionHelperExecuted','gameExecuted')
    $source=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-source-native-proof.json')
    $sourceResult=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-source-native-result.json')
    Assert-EnginePackage ($source.schemaVersion -eq 1 -and $source.kind -ceq 'owned-native-tween-source-fixture-build' -and
        $source.passed -eq $true -and $source.compileExitCode -eq 0 -and $source.sourceHashesUnchanged -eq $true -and
        $source.result.sha256 -ceq $evidence['tween-source-native-result.json'] -and
        $source.sourceArchive.sha256 -ceq $evidence['tween-source-native-sources.zip'] -and
        $source.sourceArchive.bytes -eq (Get-Item -LiteralPath (Join-Path $evidenceRoot 'tween-source-native-sources.zip')).Length -and
        @($source.sourceFiles).Count -eq 14 -and @($source.modes).Count -eq 13) 'actual owned Tween source fixture proof/result/archive relations differ.'
    Assert-EnginePackage ($sourceResult.schemaVersion -eq 1 -and $sourceResult.kind -ceq 'owned-native-tween-source-fixture-summary' -and
        $sourceResult.passed -eq $true -and $sourceResult.independentProcesses -eq 13 -and $sourceResult.checks -gt 0 -and
        $sourceResult.sourceHashesUnchanged -eq $true -and $sourceResult.actualPreservingEntryExecuted -eq $true -and
        $sourceResult.sourceArchive.sha256 -ceq $source.sourceArchive.sha256 -and @($sourceResult.modes).Count -eq 13) 'thirteen actual owned Tween source processes are required.'
    Assert-EngineFalseFields $source @('productionHelperExecuted','gameExecuted','officialMetadataVerified')
    Assert-EngineFalseFields $sourceResult @('productionHelperExecuted','gameExecuted','officialMetadataVerified')
    $retirement=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-retirement-native-proof.json')
    Assert-EnginePackage ($retirement.schemaVersion -eq 1 -and $retirement.kind -ceq 'owned-tween-retirement-native-build' -and
        $retirement.passed -eq $true -and $retirement.compileExitCode -eq 0 -and $retirement.sourceHashesUnchanged -eq $true -and
        @($retirement.sources).Count -eq 14 -and @($retirement.modes).Count -eq 18 -and
        $retirement.sourceArchive.sha256 -ceq $evidence['tween-retirement-native-sources.zip']) 'actual retirement execution/source proof differs.'
    Assert-EngineFalseFields $retirement @('gameExecuted','productionHelperExecuted')
    $references=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-references-native-proof.json')
    $referencesResult=Read-EnginePackageJson (Join-Path $evidenceRoot 'tween-references-native-result.json')
    Assert-EnginePackage ($references.schemaVersion -eq 1 -and $references.kind -ceq 'owned-tween-source-references-native-build' -and
        $references.passed -eq $true -and $references.compileExitCode -eq 0 -and $references.sourceHashesUnchanged -eq $true -and
        @($references.sourceFiles).Count -eq 7 -and $references.result.sha256 -ceq $evidence['tween-references-native-result.json'] -and
        $references.sourceArchive.sha256 -ceq $evidence['tween-references-native-sources.zip'] -and
        $referencesResult.passed -eq $true -and $referencesResult.independentProcesses -eq 9) 'actual reference execution/source proof differs.'
    Assert-EngineFalseFields $references @('gameExecuted','productionHelperExecuted','officialMetadataVerified','actualPublisherReadyVerified')
    return [pscustomobject]@{Root=$root;PackageRoot=$payloadRoot;Manifest=$manifest;Payload=$payload;FinalNative=$final;ManagedBuild=$managed}
}
function Test-EngineTransportPackagePe([string]$CandidateDirectory,[string]$GameRoot,[string]$Python='python') {
    $candidate=Get-EngineTransportProductionCandidatePackage $CandidateDirectory
    $game=Assert-EngineOrdinaryPath $GameRoot -Directory
    $lines=@(& $Python -B (Join-Path $PSScriptRoot 'verify-engine-transport-production-package.py') --package-directory $candidate.Root --game-directory $game --print-json)
    Assert-EnginePackage ($LASTEXITCODE -eq 0) 'actual relocated final PE validation failed.'
    $result=($lines -join "`n")|ConvertFrom-Json -AsHashtable
    Assert-EnginePackage ($result.passed -eq $true -and $result.kind -ceq 'engine-transport-relocated-package-validation' -and $result.finalGraphComplete -eq $true) 'relocated final PE validation receipt differs.'
    return $result
}
function Get-EngineTransportCandidateInstallPlan([string]$CandidateDirectory,[string]$GameRoot,[hashtable]$Owned,[string]$Python='python') {
    $package=Get-EngineTransportProductionCandidatePackage $CandidateDirectory
    $game=Assert-EngineOrdinaryPath $GameRoot -Directory
    # GameAssembly is a read-only supported-build input, never loader-owned
    # payload. Do not broaden Get-InstallTarget's writable root whitelist.
    $gameAssembly=Assert-EngineOrdinaryPath (Join-Path $game 'GameAssembly.dll')
    Assert-EnginePackage ((Get-EnginePackageHash $gameAssembly) -ceq '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79') 'unsupported actual GameAssembly.dll fingerprint.'
    Assert-EnginePackage ($package.Root -ine $game -and -not $package.Root.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
        -not $game.StartsWith($package.Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'candidate and installation roots overlap.'
    $validation=Test-EngineTransportPackagePe $package.Root $game $Python
    $plan=@($package.Manifest.files|ForEach-Object{[pscustomobject]@{source=(Get-EnginePackageFile $package.PackageRoot $_.path);path=$_.path.Replace('/','\');sha256=$_.sha256}})
    Assert-InstallPlan $game $plan $Owned
    $removePaths=@('BepInEx\plugins\RizToolbox\RizToolbox.EngineNodeKernel.dll')
    Assert-InstallRemovalPlan $game $removePaths $Owned $plan
    return [pscustomobject]@{Package=$package;Plan=$plan;Validation=$validation;RemovePaths=$removePaths}
}
