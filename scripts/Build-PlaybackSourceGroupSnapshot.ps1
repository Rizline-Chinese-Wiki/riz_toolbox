#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSnapshotManifest,[Parameter(Mandatory)][string]$NativeBuildManifest,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath='C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll',
    [string]$GetTimePackageDirectory='D:/riz-build-cache-next/single-return-managed-native-r4-20261030')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 28),[Text.UTF8Encoding]::new($false))}
function ContainedRelative([string]$Root, [string]$Path) {
    $relative = [IO.Path]::GetRelativePath($Root, $Path)
    if ([IO.Path]::IsPathFullyQualified($relative) -or $relative -eq '..' -or $relative.StartsWith('../') -or $relative.StartsWith('..\')) {
        throw "A declared input escapes its frozen source directory: $Path"
    }
    return $relative
}
function ManagedSymbolProof([string]$Assembly, [string]$SourceRoot, [hashtable]$FrozenHashes) {
    $pdb = [IO.Path]::ChangeExtension($Assembly, '.pdb')
    $peReader = $null; $provider = $null; $peStream = $null; $pdbStream = $null
    try {
        $peStream = [IO.File]::OpenRead($Assembly)
        $pdbStream = [IO.File]::OpenRead($pdb)
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($peStream)
        $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $pdbReader = $provider.GetMetadataReader()
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
        $module = $metadata.GetModuleDefinition()
        $codeViews = @($peReader.ReadDebugDirectory() | Where-Object Type -eq 'CodeView')
        if ($codeViews.Count -ne 1) { throw "Expected one actual PE CodeView record: $Assembly" }
        $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViews[0])
        $id = [byte[]]$pdbReader.DebugMetadataHeader.Id
        if ($id.Length -ne 20 -or $codeView.Age -ne 1 -or
            $codeView.Guid -ne [guid]::new([byte[]]$id[0..15]) -or $codeViews[0].Stamp -ne [BitConverter]::ToUInt32($id, 16)) {
            throw "The actual PE does not identify its adjacent portable PDB: $Assembly"
        }
        $documents = @()
        foreach ($handle in $pdbReader.Documents) {
            $document = $pdbReader.GetDocument($handle)
            $name = $pdbReader.GetString($document.Name)
            $prefix = '/_/riz_toolbox/'
            if (-not $name.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "Unmapped PDB document: $name" }
            $path = [IO.Path]::GetFullPath((Join-Path $SourceRoot $name.Substring($prefix.Length)))
            $relative = ContainedRelative $SourceRoot $path
            $expected = [Convert]::ToHexString($pdbReader.GetBlobBytes($document.Hash)).ToLowerInvariant()
            if ($pdbReader.GetGuid($document.HashAlgorithm).ToString() -ne '8829d00f-11b8-4213-878b-770e8597ac16' -or
                -not (Test-Path -LiteralPath $path -PathType Leaf) -or (Hash $path) -ne $expected) {
                throw "A portable PDB source checksum differs from the source actually built: $name"
            }
            $frozen = $FrozenHashes.ContainsKey($path)
            $generated = $relative.Replace('\', '/').Contains('/obj/')
            if ($frozen -and $FrozenHashes[$path] -ne $expected -or -not $frozen -and -not $generated) {
                throw "A PDB document is neither an exact frozen input nor a generated build file: $name"
            }
            $documents += ,[ordered]@{ document = $name; path = $path; sha256 = $expected; frozenInput = $frozen; generatedByBuild = $generated }
        }
        return [ordered]@{
            assembly = [ordered]@{ path = $Assembly; sha256 = Hash $Assembly }
            pdb = [ordered]@{ path = $pdb; sha256 = Hash $pdb }
            mvid = $metadata.GetGuid($module.Mvid).ToString(); codeViewGuid = $codeView.Guid.ToString()
            codeViewStamp = $codeViews[0].Stamp; codeViewAge = $codeView.Age
            portablePdbId = [Convert]::ToHexString($id).ToLowerInvariant()
            codeViewMatchesPortablePdb = $true; documentChecksumsMatch = $true
            documentCount = $documents.Count; frozenDocumentCount = @($documents | Where-Object frozenInput).Count
            documents = $documents
        }
    } finally {
        if ($provider) { $provider.Dispose() }; if ($peReader) { $peReader.Dispose() }
        if ($pdbStream) { $pdbStream.Dispose() }; if ($peStream) { $peStream.Dispose() }
    }
}
$proof=Get-Content -LiteralPath $SourceSnapshotManifest -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $NativeBuildManifest -Raw|ConvertFrom-Json
if($proof.kind -ne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true -or $proof.runnerExitCode -ne 0){throw 'An actual current source snapshot proof is required.'}
if($native.kind -ne 'independent-stock-shaped-source-prototype' -or $native.passed -ne $true -or $native.actualExitCode -ne 0){throw 'An actual exact source helper and owned fixture build is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $output.StartsWith('D:\riz-build-cache-next\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){throw 'Use a fresh isolated D-drive output directory.'}
if((Hash $DobbyPath) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'){throw 'The inspected actual Dobby image differs.'}
$snapshot=Join-Path (Split-Path -Parent $SourceSnapshotManifest) 'source-snapshot'
$source=Join-Path $output 'source';$nativeRoot=Split-Path -Parent $NativeBuildManifest
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{kind='frozen-eleven-member-playback-source-group';passed=$false;buildExitCode=$null;testExitCode=$null;failure=$null
    sourceProof=[ordered]@{path=$SourceSnapshotManifest;sha256=Hash $SourceSnapshotManifest}
    nativeProof=[ordered]@{path=$NativeBuildManifest;sha256=Hash $NativeBuildManifest}
    sources=@();inputs=@();nativeInputs=@();managedSymbols=@();artifacts=@();child=$null
    physicalMemberCount=11;sourceMemberCount=10;sharedSourceHubCount=1;gameCodeExecuted=$false;installed=$false;productionAdmission=$false}
try{
    $frozenHashes=@{}
    foreach($row in $proof.snapshotFiles){
        if((Hash $row.path) -ne $row.sha256){throw "A frozen current source input changed: $($row.path)"}
        $relative=ContainedRelative $snapshot $row.path;$destination=Join-Path $source $relative
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        if((Hash $destination) -ne $row.sha256){throw 'Copied source differs.'}
        $result.sources+=,[ordered]@{source=$row.path;path=$destination;sha256=$row.sha256};$frozenHashes[$destination]=$row.sha256
    }
    foreach($path in @($PSCommandPath,(Join-Path $PSScriptRoot 'Test-PlaybackSourceGroup.ps1'))){
        $destination=Join-Path $output ('scripts/'+[IO.Path]::GetFileName($path));$null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $path -Destination $destination;$result.inputs+=,[ordered]@{source=$path;path=$destination;sha256=Hash $path}
    }
    foreach($row in @($native.sources)+@($native.frozenSources)+@($native.files)){if((Hash $row.path) -ne $row.sha256){throw "A declared exact native input or output changed: $($row.path)"}}
    $packages=[ordered]@{source=$nativeRoot;gettime=$GetTimePackageDirectory}
    foreach($key in $packages.Keys){foreach($file in Get-ChildItem -LiteralPath $packages[$key] -File -Recurse){
        $before=Hash $file.FullName;$destination=Join-Path $output ('native-inputs/'+$key+'/'+(ContainedRelative $packages[$key] $file.FullName))
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force;Copy-Item -LiteralPath $file.FullName -Destination $destination
        if((Hash $destination) -ne $before -or (Hash $file.FullName) -ne $before){throw 'Copied native package changed.'}
        $result.nativeInputs+=,[ordered]@{source=$file.FullName;path=$destination;sha256=$before}
    }}
    $dobby=Join-Path $output 'native-inputs/dobby.dll';Copy-Item -LiteralPath $DobbyPath -Destination $dobby
    $result.nativeInputs+=,[ordered]@{source=$DobbyPath;path=$dobby;sha256=Hash $dobby}
    Json $result.sources (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'source-snapshot.zip';[IO.Compression.ZipFile]::CreateFromDirectory($source,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    $result.sourceArchive=[ordered]@{path=$archive;sha256=Hash $archive}
    $config=Join-Path $output 'NuGet.Config';[IO.File]::WriteAllText($config,'<configuration><packageSources><clear/></packageSources></configuration>')
    $project=Join-Path $source 'tests/RizToolbox.PlaybackSourceGroup.Tests/RizToolbox.PlaybackSourceGroup.Tests.csproj'
    & (Get-RizDotnetPath) build $project -c Release -m:1 --configfile $config -p:UseSharedCompilation=false --nologo -v:minimal 2>&1|Out-File -LiteralPath (Join-Path $output 'build.log')
    $result.buildExitCode=$LASTEXITCODE;if($LASTEXITCODE -ne 0){throw 'The actual frozen eleven-member group build failed.'}
    $binary=Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0'
    foreach($name in @('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Gameplay.Tests')){
        $result.managedSymbols+=,(ManagedSymbolProof (Join-Path $binary ($name+'.dll')) $source $frozenHashes)
    }
    Json $result.managedSymbols (Join-Path $output 'managed-symbol-source-proof.json')
    & 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File (Join-Path $output 'scripts/Test-PlaybackSourceGroup.ps1') -ManagedAssembly (Join-Path $binary 'RizToolbox.Gameplay.Tests.dll') -FixturePath (Join-Path $output 'native-inputs/source/RizToolbox.PlaybackStockFixture.dll') -GetTimeHelperPath (Join-Path $output 'native-inputs/gettime/RizToolbox.SingleReturn.Prototype.dll') -SourceHelperPath (Join-Path $output 'native-inputs/source/RizToolbox.PlaybackSourcePrototype.dll') -DobbyPath $dobby -OutputDirectory (Join-Path $output 'checks') 2>&1|Out-File -LiteralPath (Join-Path $output 'checks.log')
    $result.testExitCode=$LASTEXITCODE
    $result.child=Get-Content -LiteralPath (Join-Path $output 'checks/test-result.json') -Raw|ConvertFrom-Json
    if($result.testExitCode -ne 0 -or $result.child.allPassed -ne $true){throw 'An actual frozen eleven-member group child failed.'}
    foreach($row in @($result.sources)+@($result.inputs)+@($result.nativeInputs)+@($result.sourceProof)+@($result.nativeProof)){
        if((Hash $row.path) -ne $row.sha256){throw "A frozen eleven-member input changed: $($row.path)"}
    }
    foreach($row in $result.managedSymbols){foreach($item in @($row.assembly)+@($row.pdb)+@($row.documents)){if((Hash $item.path) -ne $item.sha256){throw 'A PE/PDB/source hash changed.'}}}
    Json $result.sources (Join-Path $output 'source-after.json');$result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    $result.artifacts=@(Get-ChildItem -LiteralPath $output -File -Recurse|Where-Object{$_.FullName -notlike ($source+'*') -and $_.FullName -ne (Join-Path $output 'test-result.json')}|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    Json $result (Join-Path $output 'test-result.json')
}
Write-Output "Frozen eleven-member group: $($result.child.processes) processes/$($result.child.checks) checks, including pre-capture Commit-zero refusals."
