#requires -Version 7.2
Set-StrictMode -Version Latest

function Assert-EnginePackage([bool]$Condition,[string]$Message) { if(-not $Condition){throw "Candidate package: $Message"} }
function Get-EnginePackageHash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Assert-EngineOrdinaryPath([string]$Path,[switch]$Directory,[switch]$MayNotExist) {
    Assert-EnginePackage ([IO.Path]::IsPathFullyQualified($Path)) 'absolute paths are required.'
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\','/');$cursor=$full
    while($cursor){
        if(Test-Path -LiteralPath $cursor){Assert-EnginePackage (-not ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) "linked path refused: $cursor"}
        $parent=Split-Path -Parent $cursor;if($parent -eq $cursor){break};$cursor=$parent
    }
    if(-not $MayNotExist){Assert-EnginePackage (Test-Path -LiteralPath $full -PathType $(if($Directory){'Container'}else{'Leaf'})) "ordinary path is missing: $full"}
    return $full
}
function Get-EnginePackageFile([string]$Root,[string]$Relative) {
    Assert-EnginePackage ($Relative -cmatch '^[A-Za-z0-9_.\-/]+$' -and -not $Relative.Contains('\') -and -not [IO.Path]::IsPathRooted($Relative)) 'noncanonical relative path.'
    Assert-EnginePackage (@($Relative.Split('/')|Where-Object{$_ -in @('','.','..') -or $_.TrimEnd(' ','.') -cne $_}).Count -eq 0) 'unsafe relative path.'
    $full=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    Assert-EnginePackage ($full.StartsWith($Root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'path escaped its root.'
    return Assert-EngineOrdinaryPath $full
}
function Get-EngineDefaultPackagePaths {
    $paths=@('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader')|ForEach-Object{"BepInEx/plugins/RizToolbox/$_.dll"}
    $paths+=@('BepInEx/plugins/RizToolbox/profiles/steam-25977441.json','BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll',
        'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll','winhttp.dll','.doorstop_version',
        'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll','BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll',
        'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip','BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt')
    foreach($id in @('example.panel','example.events','example.color-low','example.color-high','example.scores')){$paths+="BepInEx/RizToolbox/mods/$id/plugin.json","BepInEx/RizToolbox/mods/$id/RizToolbox.Examples.dll"}
    return $paths
}
function Get-EngineCandidatePackagePaths {
    return @((Get-EngineDefaultPackagePaths)|Where-Object{$_ -cne 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'})+@('RizToolbox.EngineNodeKernel.dll','RizToolbox.EngineBootstrap.dll')
}
function Get-EngineCandidateEvidencePaths {
    return @('production-build-manifest.json','production-native-profile.json','production-managed-profile.json','managed-build-manifest.json',
        'doorstop-manifest.json','EngineBootstrapProductionProfile.h','NativeEngineProductionProfile.Generated.cs','doorstop.map','bootstrap.map','base-package-manifest.json')
}
function Assert-EnginePackageTree([string]$Root,[string[]]$Expected) {
    $null=Assert-EngineOrdinaryPath $Root -Directory
    $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($relative in $Expected){Assert-EnginePackage ($allowed.Add($relative)) 'duplicate expected path.';$parts=$relative.Split('/');for($i=1;$i -lt $parts.Count;++$i){$null=$directories.Add(($parts[0..($i-1)] -join '/'))}}
    $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($Root);$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    while($queue.Count){$directory=$queue.Dequeue();foreach($item in Get-ChildItem -LiteralPath $directory -Force){
        Assert-EnginePackage (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) "linked package entry: $($item.FullName)"
        $relative=[IO.Path]::GetRelativePath($Root,$item.FullName).Replace('\','/')
        if($item.PSIsContainer){Assert-EnginePackage ($directories.Contains($relative)) "unexpected package directory: $relative";$queue.Enqueue($item.FullName)}
        else{Assert-EnginePackage ($allowed.Contains($relative) -and $seen.Add($relative)) "unexpected package file: $relative"}
    }}
    Assert-EnginePackage ($seen.Count -eq $allowed.Count) 'package files are missing.'
}
function Read-EnginePackageJson([string]$Path) {
    $null=Assert-EngineOrdinaryPath $Path
    $data=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json -AsHashtable -Depth 50
    Assert-EnginePackage ($data -is [Collections.IDictionary]) "JSON object required: $Path"
    return $data
}
function Assert-EngineFalseFields($Data,[string[]]$Names) {
    foreach($name in $Names){Assert-EnginePackage ($Data.Contains($name) -and $Data[$name] -is [bool] -and -not $Data[$name]) "'$name' must remain false; package metadata cannot grant admission."}
}
function Assert-EngineFileRows($Rows,[string[]]$Expected,[string]$Root,[switch]$Absolute) {
    $map=@{};Assert-EnginePackage (@($Rows).Count -eq $Expected.Count) 'file-list count differs.'
    foreach($row in $Rows){
        Assert-EnginePackage ($row -is [Collections.IDictionary] -and $row.Contains('path') -and $row.Contains('sha256') -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'invalid file/hash row.'
        $relative=if($Absolute){Assert-EnginePackage ([IO.Path]::IsPathFullyQualified($row.path)) 'build file is not absolute.';[IO.Path]::GetRelativePath($Root,[IO.Path]::GetFullPath($row.path)).Replace('\','/')}else{$row.path}
        Assert-EnginePackage ($relative -cin $Expected -and -not $map.ContainsKey($relative)) "unexpected/duplicate file: $relative"
        $path=Get-EnginePackageFile $Root $relative;Assert-EnginePackage ((Get-EnginePackageHash $path) -ceq $row.sha256) "file hash mismatch: $relative"
        $map[$relative]=$row.sha256
    }return $map
}
function Assert-EngineDeclaredSources($Rows,[string]$Repository) {
    Assert-EnginePackage (@($Rows).Count -gt 0) 'declared source freeze is missing.';$seen=@{}
    foreach($row in $Rows){
        Assert-EnginePackage ($row.path -is [string] -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'invalid declared source.'
        $path=Assert-EngineOrdinaryPath $row.path
        Assert-EnginePackage ($path.StartsWith($Repository.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and -not $seen.ContainsKey($path)) 'declared source escaped repository or is duplicated.'
        Assert-EnginePackage ((Get-EnginePackageHash $path) -ceq $row.sha256) "declared build source changed: $path";$seen[$path]=$true
    }
}
function Assert-EngineDoorstopSourceArchive([string]$Path,$Doorstop,[string]$LicenseHash) {
    $expected=@{};$groups=@(@($Doorstop.canonicalFiles),@($Doorstop.engineBootstrapFiles))
    Assert-EnginePackage ($groups[0].Count -eq 5 -and $groups[1].Count -eq 3) 'canonical corresponding-source rows are missing.'
    $names=@('riz_winhttp.h','riz_winhttp.c','riz_winhttp_entry.asm','riz_winhttp_doorstop.h','riz_winhttp_doorstop.c','riz_engine_bootstrap.h','riz_engine_bootstrap.c','riz_engine_bootstrap.def')|ForEach-Object{"src/windows/$_"}
    foreach($rows in $groups){foreach($row in $rows){Assert-EnginePackage ($row.path -cin $names -and $row.sha256 -cmatch '^[0-9a-f]{64}$' -and -not $expected.ContainsKey($row.path)) 'invalid corresponding-source row.';$expected[$row.path]=$row.sha256}}
    $expected['LICENSE']=$LicenseHash;$seen=@{};$archive=[IO.Compression.ZipFile]::OpenRead($Path)
    try{foreach($entry in $archive.Entries){
        $name=$entry.FullName.Replace('\','/').TrimEnd('/');if(-not $name){continue}
        Assert-EnginePackage (-not [IO.Path]::IsPathRooted($name) -and -not $name.Contains(':') -and @($name.Split('/')|Where-Object{$_ -in @('','.','..')}).Count -eq 0 -and -not $seen.ContainsKey($name) -and (($entry.ExternalAttributes -shr 16) -band 0xf000) -ne 0xa000) 'unsafe/linked/duplicate corresponding-source archive entry.'
        $seen[$name]=$true
        if($expected.ContainsKey($name)){
            Assert-EnginePackage ($entry.Length -gt 0 -and $entry.Length -lt 1048576) 'canonical source archive entry length differs.'
            $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()}finally{$sha.Dispose();$stream.Dispose()}
            Assert-EnginePackage ($hash -ceq $expected[$name]) "corresponding source differs: $name"
        }
    };foreach($name in $expected.Keys){Assert-EnginePackage ($seen.ContainsKey($name)) "corresponding source missing: $name"}}finally{$archive.Dispose()}
}
function Get-EngineCandidateRelations([string]$EvidenceRoot,[hashtable]$Payload,[switch]$CheckLiveSources,[string]$Repository) {
    $build=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'production-build-manifest.json')
    $native=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'production-native-profile.json')
    $managed=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'production-managed-profile.json')
    $managedBuild=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'managed-build-manifest.json')
    $doorstop=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'doorstop-manifest.json')
    $base=Read-EnginePackageJson (Get-EnginePackageFile $EvidenceRoot 'base-package-manifest.json')
    Assert-EnginePackage ($build.kind -ceq 'fixed-game-engine-production-candidate-build' -and $build.schemaVersion -eq 1 -and $build.managedBuilt -is [bool] -and $build.managedBuilt) 'complete candidate build required.'
    Assert-EngineFalseFields $build @('productionAdmissionProved','gameExecuted','installed')
    Assert-EnginePackage ($build.nativeProfile -ceq 'production-native-profile.json' -and $build.managedProfile -ceq 'production-managed-profile.json') 'profile names differ.'
    Assert-EnginePackage ($native.kind -ceq 'fixed-game-engine-production-compile-facts' -and $native.schemaVersion -eq 1) 'native facts schema differs.'
    Assert-EnginePackage ($managed.kind -ceq 'fixed-game-engine-managed-compile-facts' -and $managed.schemaVersion -eq 1) 'managed facts schema differs.'
    foreach($data in @($native,$managed)){Assert-EngineFalseFields $data @('productionAdmissionGranted','executionWindowProved','targetImagesLoaded','installed')}
    Assert-EnginePackage ($managedBuild.kind -ceq 'fixed-game-engine-managed-candidate-build' -and $managedBuild.schemaVersion -eq 1) 'managed compilation freeze required.'
    Assert-EngineFalseFields $managedBuild @('productionAdmissionProved','gameExecuted','installed')
    $nativeNames=@('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','doorstop.map','bootstrap.map','production-native-profile.json','production-managed-profile.json')
    Assert-EnginePackage (@($build.files).Count -eq 7) 'native build file count differs.';$buildHashes=@{};$buildRoot=$null
    foreach($row in $build.files){
        Assert-EnginePackage ([IO.Path]::IsPathFullyQualified($row.path) -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'invalid native build row.'
        $name=[IO.Path]::GetFileName($row.path);$parent=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($row.path))
        if(-not $buildRoot){$buildRoot=$parent};Assert-EnginePackage ($parent -ieq $buildRoot -and $name -cin $nativeNames -and -not $buildHashes.ContainsKey($name)) 'native outputs do not share an exact build root.'
        $buildHashes[$name]=$row.sha256
        $actual=if($Payload.ContainsKey($name)){$Payload[$name]}else{Get-EnginePackageHash (Get-EnginePackageFile $EvidenceRoot $name)}
        Assert-EnginePackage ($actual -ceq $row.sha256) "native artifact/profile relationship differs: $name"
    }
    $gameHashes=@{'Rizline.exe'='f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e';'UnityPlayer.dll'='43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1';'GameAssembly.dll'='853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'}
    Assert-EnginePackage ($native.images.Count -eq 5) 'native image set differs.'
    foreach($name in $gameHashes.Keys){Assert-EnginePackage ($native.images.Contains($name) -and $native.images[$name].sha256 -ceq $gameHashes[$name] -and $native.images[$name].module -ceq $name) "fixed game identity differs: $name"}
    Assert-EnginePackage ($native.images.Kernel.sha256 -ceq '923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662' -and $native.images.Kernel.sha256 -ceq $Payload['RizToolbox.EngineNodeKernel.dll']) 'fixed Kernel differs.'
    foreach($pair in @(@('Doorstop','winhttp.dll'),@('Kernel','RizToolbox.EngineNodeKernel.dll'))){$image=$native.images[$pair[0]];Assert-EnginePackage ($image.module -ceq $pair[1] -and $image.sha256 -ceq $Payload[$pair[1]] -and [IO.Path]::GetFullPath($image.path) -ieq (Join-Path $buildRoot $pair[1])) 'native compile image relationship differs.'}
    Assert-EnginePackage ($managed.bootstrap.module -ceq 'RizToolbox.EngineBootstrap.dll' -and $managed.bootstrap.sha256 -ceq $Payload['RizToolbox.EngineBootstrap.dll'] -and [IO.Path]::GetFullPath($managed.bootstrap.path) -ieq (Join-Path $buildRoot 'RizToolbox.EngineBootstrap.dll')) 'managed Bootstrap pin differs.'
    foreach($link in @(@($native.generatedHeader,'EngineBootstrapProductionProfile.h'),@($managed.generatedSource,'NativeEngineProductionProfile.Generated.cs'),@($managed.nativeProfile,'production-native-profile.json'))){
        Assert-EnginePackage ([IO.Path]::GetFullPath($link[0].path) -ieq (Join-Path $buildRoot $link[1]) -and $link[0].sha256 -ceq (Get-EnginePackageHash (Get-EnginePackageFile $EvidenceRoot $link[1]))) 'generated compile input relationship differs.'
    }
    Assert-EnginePackage ($managedBuild.generatedSource.path -ceq $managed.generatedSource.path -and $managedBuild.generatedSource.sha256 -ceq $managed.generatedSource.sha256) 'managed build used a different generated profile.'
    $six=@('Sdk','Core','Runtime','Adapter','Hosting','Loader');$seen=@{};Assert-EnginePackage (@($managedBuild.files).Count -eq 6) 'exactly six managed outputs required.'
    foreach($row in $managedBuild.files){$name=[IO.Path]::GetFileName($row.path);Assert-EnginePackage ($name -cin @($six|ForEach-Object{"RizToolbox.$_.dll"}) -and -not $seen.ContainsKey($name) -and [IO.Path]::GetFullPath($row.path) -ieq (Join-Path $buildRoot "managed/$name") -and $row.sha256 -ceq $Payload["BepInEx/plugins/RizToolbox/$name"]) 'managed output relationship differs.';$seen[$name]=$true}
    Assert-EnginePackage ($doorstop.profile -ceq 'fixed-system-winhttp-engine-bootstrap-candidate' -and $doorstop.engineBootstrap -is [bool] -and $doorstop.engineBootstrap -and $doorstop.engineBootstrapPublicationVersion -eq 4 -and $doorstop.engineBootstrapProfile -eq 2 -and $doorstop.publicExportCount -eq 82) 'ordinary Bootstrap Doorstop build required.'
    $abi=@('GetVersion','GetInfoV4','VerifyV4','AbortAfterPublication')|ForEach-Object{"RizEngineFirstInstall_$_"}
    Assert-EnginePackage ($doorstop.engineBootstrapOrdinaryImportRequired -eq $true -and @($doorstop.engineBootstrapAbiNames).Count -eq 4 -and @($doorstop.engineBootstrapAbiNames|Sort-Object -Unique).Count -eq 4 -and @($doorstop.engineBootstrapAbiNames|Where-Object{$_ -cnotin $abi}).Count -eq 0 -and $doorstop.engineBootstrapReadyAdmissionMask -eq 65280) 'ordinary Bootstrap ABI/admission contract differs.'
    Assert-EngineFalseFields $doorstop @('installed','gameRuntimeVerified','engineBootstrapProductionAdmissionVerified')
    $ds=@{};foreach($row in $doorstop.files){Assert-EnginePackage ($row.sha256 -cmatch '^[0-9a-f]{64}$' -and -not $ds.ContainsKey($row.path)) 'invalid Doorstop evidence row.';$ds[$row.path]=$row.sha256}
    Assert-EnginePackage ($ds['winhttp.dll'] -ceq $Payload['winhttp.dll'] -and $ds['doorstop.map'] -ceq $buildHashes['doorstop.map'] -and $ds['doorstop-corresponding-source.zip'] -ceq $Payload['BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip'] -and $ds['UnityDoorstop.LGPL-2.1.txt'] -ceq $Payload['BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt']) 'Doorstop corresponding source/license relationship differs.'
    Assert-EngineDoorstopSourceArchive (Join-Path (Split-Path -Parent $EvidenceRoot) 'package/BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip') $doorstop $Payload['BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt']
    $basePaths=@(Get-EngineDefaultPackagePaths);Assert-EnginePackage ($base.schemaVersion -eq 2 -and @($base.files).Count -eq 23) 'the original 23-file package manifest is required.';$baseHashes=@{}
    foreach($row in $base.files){Assert-EnginePackage ($row.path -cin $basePaths -and -not $baseHashes.ContainsKey($row.path) -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'invalid base package entry.';$baseHashes[$row.path]=$row.sha256}
    $replaced=@('winhttp.dll','.doorstop_version','BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll','BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip','BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt')+@($six|ForEach-Object{"BepInEx/plugins/RizToolbox/RizToolbox.$_.dll"})
    foreach($path in $basePaths|Where-Object{$_ -cnotin $replaced}){Assert-EnginePackage ($baseHashes[$path] -ceq $Payload[$path]) "inherited base payload differs: $path"}
    $expectedEdges=@('3/2/UnityMain2')+@('WinHttpOpen','WinHttpGetProxyForUrl','WinHttpGetIEProxyConfigForCurrentUser','WinHttpCloseHandle'|ForEach-Object{"2/4/$_"})+@('GetVersion','GetInfoV4','VerifyV4','AbortAfterPublication'|ForEach-Object{"4/0/RizEngineFirstInstall_$_"})+@('GetAbiVersion','GetLimit','GetFrameSize','GetCompletionSize','GetEntry','Configure','Attach','GetInfo'|ForEach-Object{"0/1/RizEngineNodeContext_$_"})
    $edges=@{};Assert-EnginePackage (@($native.edges).Count -eq 17) 'fixed dependency edge count differs.'
    foreach($edge in $native.edges){$key="$($edge.source)/$($edge.destination)/$($edge.symbol)";Assert-EnginePackage ($key -cin $expectedEdges -and -not $edges.ContainsKey($key)) 'fixed dependency edge differs.';$edges[$key]=$true}
    if($CheckLiveSources){Assert-EngineDeclaredSources $build.sources $Repository;Assert-EngineDeclaredSources $managedBuild.sources $Repository}
    return [pscustomobject]@{Build=$build;ManagedBuild=$managedBuild;Native=$native;Managed=$managed;BuildRoot=$buildRoot}
}
function Get-EngineProductionCandidatePackage([string]$CandidateDirectory) {
    $root=Assert-EngineOrdinaryPath $CandidateDirectory -Directory
    $manifest=Read-EnginePackageJson (Get-EnginePackageFile $root 'candidate-package-manifest.json')
    Assert-EnginePackage ($manifest.kind -ceq 'fixed-game-engine-production-candidate-package' -and $manifest.schemaVersion -eq 1) 'candidate package schema differs.'
    Assert-EngineFalseFields $manifest @('productionAdmissionGranted','executionWindowProved','gameExecuted','installed')
    $payloadPaths=@(Get-EngineCandidatePackagePaths);$evidencePaths=@(Get-EngineCandidateEvidencePaths)
    $null=Assert-EngineFileRows $manifest.evidence $evidencePaths (Join-Path $root 'evidence')
    $payload=Assert-EngineFileRows $manifest.files $payloadPaths (Join-Path $root 'package')
    Assert-EnginePackageTree $root (@('candidate-package-manifest.json')+@($payloadPaths|ForEach-Object{"package/$_"})+@($evidencePaths|ForEach-Object{"evidence/$_"}))
    $relations=Get-EngineCandidateRelations (Join-Path $root 'evidence') $payload
    Assert-EnginePackage ([IO.File]::ReadAllText((Join-Path $root 'package/.doorstop_version')) -ceq '4.5.0-riz-engine-candidate.1') 'candidate marker differs.'
    return [pscustomobject]@{Root=$root;PackageRoot=(Join-Path $root 'package');Manifest=$manifest;Payload=$payload;Relations=$relations}
}
function Get-EngineCandidateInstallPlan([string]$CandidateDirectory,[string]$GameRoot,[hashtable]$Owned) {
    $package=Get-EngineProductionCandidatePackage $CandidateDirectory
    $null=Assert-EngineOrdinaryPath $GameRoot -Directory
    Assert-EnginePackage (-not $package.Root.StartsWith($GameRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and -not $GameRoot.StartsWith($package.Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and $package.Root -ine $GameRoot) 'candidate and installation roots overlap.'
    $plan=@($package.Manifest.files|ForEach-Object{[pscustomobject]@{source=(Get-EnginePackageFile $package.PackageRoot $_.path);path=$_.path.Replace('/','\');sha256=$_.sha256}})
    Assert-InstallPlan $GameRoot $plan $Owned
    return [pscustomobject]@{Package=$package;Plan=$plan;RemovePaths=@('BepInEx\plugins\RizToolbox\RizToolbox.EngineNodeKernel.dll')}
}
