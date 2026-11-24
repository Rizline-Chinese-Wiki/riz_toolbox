#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceSnapshotManifest,
    [Parameter(Mandatory)][string]$NativeBuildManifest,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameAssemblyPath = 'D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll',
    [string]$DobbyPath = 'C:/Users/71957/Desktop/riz_toolbox/.tools/bepinex-789/BepInEx/core/dobby.dll',
    [string]$GetTimePackageDirectory = 'D:/riz-build-cache-next/single-return-managed-native-r4-20261030',
    [string]$FoundationHelperDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
foreach ($path in @($SourceSnapshotManifest, $NativeBuildManifest, $OutputDirectory, $GameAssemblyPath, $DobbyPath, $GetTimePackageDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Absolute paths are required.' }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Json([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 24), [Text.UTF8Encoding]::new($false))
}
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
$proof = Get-Content -LiteralPath $SourceSnapshotManifest -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $NativeBuildManifest -Raw | ConvertFrom-Json
if ($proof.kind -ne 'active-retirement-snapshot-checks' -or $proof.passed -ne $true -or $proof.runnerExitCode -ne 0) {
    throw 'An actual successful current source snapshot is required.'
}
if ($native.kind -ne 'independent-stock-shaped-source-prototype' -or $native.passed -ne $true -or $native.actualExitCode -ne 0) {
    throw 'An actual successful exact source helper/owned fixture build is required.'
}
$fixedImageHash = '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'
if ((Hash $GameAssemblyPath) -ne $fixedImageHash -or
    (Hash $DobbyPath) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded' -or
    (Hash (Join-Path $GetTimePackageDirectory 'RizToolbox.SingleReturn.Prototype.dll')) -ne '90b6133cf6ecd98dd00cd34472d81cd91493419ea4cb06b0aa59ffb0f7f0b355') {
    throw 'The fixed original image, inspected Dobby, or GetTime helper differs.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith('D:\riz-build-cache-next\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw 'Preserve previous results; use a new D-drive output directory.'
}
$snapshot = Join-Path (Split-Path -Parent $SourceSnapshotManifest) 'source-snapshot'
$nativeDirectory = Split-Path -Parent $NativeBuildManifest
$sourceRoot = Join-Path $output 'source'
if ($FoundationHelperDirectory -and -not [IO.Path]::IsPathFullyQualified($FoundationHelperDirectory)) {
    throw 'The optional actual foundation helper package must be an absolute path.'
}
$null = New-Item -ItemType Directory -Path $output
$result = [ordered]@{
    kind = 'mapped-playback-stock-joint-profile'; passed = $false; failure = $null; buildExitCode = $null
    checks = 0; modes = @(); sources = @(); nativeInputs = @(); images = @(); artifacts = @(); managedSymbols = @()
    sourceProof = [ordered]@{ path = $SourceSnapshotManifest; sha256 = Hash $SourceSnapshotManifest }
    nativeProof = [ordered]@{ path = $NativeBuildManifest; sha256 = Hash $NativeBuildManifest }
    script = [ordered]@{ path = $PSCommandPath; sha256 = Hash $PSCommandPath }
    metadataSynthetic = $true; graphSynthetic = $true; mappedPinnedImage = $true; gameExecuted = $false
    gameDllMainExecuted = $false; installed = $false; productionAdmissionVerified = $false; original44Repeated = $false
}
try {
    if ($FoundationHelperDirectory) {
        $foundationManifest = Join-Path $FoundationHelperDirectory 'native-manifest.json'
        $foundationBuild = Get-Content -LiteralPath $foundationManifest -Raw | ConvertFrom-Json
        if ($foundationBuild.kind -ne 'judge-context-operation-native-build' -or $foundationBuild.passed -ne $true -or
            $foundationBuild.buildExitCode -ne 0 -or $foundationBuild.publicationVersion -ne 7 -or $foundationBuild.expectedExports.Count -ne 85) {
            throw 'The private foundation composition needs an actual complete 85-export current native build.'
        }
        foreach ($row in @($foundationBuild.sources) + @($foundationBuild.files) + @($foundationBuild.sourceArchive)) {
            if ((Hash $row.path) -ne $row.sha256) { throw "A private foundation build input or output changed: $($row.path)" }
        }
        $result.foundationProof = [ordered]@{ path = $foundationManifest; sha256 = Hash $foundationManifest }
    }
    $frozenHashes = @{}
    foreach ($row in $proof.snapshotFiles) {
        if ((Hash $row.path) -ne $row.sha256) { throw "The declared frozen input changed: $($row.path)" }
        $relative = ContainedRelative $snapshot $row.path
        $destination = [IO.Path]::GetFullPath((Join-Path $sourceRoot $relative))
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath $row.path -Destination $destination
        if ((Hash $destination) -ne $row.sha256) { throw "A copied source differs: $relative" }
        $result.sources += ,[ordered]@{ source = $row.path; relativePath = $relative; path = $destination; sha256 = $row.sha256 }
        $frozenHashes[$destination] = $row.sha256
    }
    Json $result.sources (Join-Path $output 'source-before.json')
    $archive = Join-Path $output 'source-snapshot.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceRoot, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $result.sourceArchive = [ordered]@{ path = $archive; sha256 = Hash $archive }
    foreach ($row in @($native.sources) + @($native.frozenSources) + @($native.files)) {
        if ((Hash $row.path) -ne $row.sha256) { throw "A declared native source/output changed: $($row.path)" }
    }
    $packages = [ordered]@{ 'gettime-r4' = $GetTimePackageDirectory; 'source-native' = $nativeDirectory }
    if ($FoundationHelperDirectory) { $packages.Add('foundation-native', $FoundationHelperDirectory) }
    foreach ($key in $packages.Keys) {
        $package = $packages[$key]
        foreach ($file in @(Get-ChildItem -LiteralPath $package -File -Recurse)) {
            $before = Hash $file.FullName
            $destination = Join-Path $output ('native-inputs/' + $key + '/' + (ContainedRelative $package $file.FullName))
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            Copy-Item -LiteralPath $file.FullName -Destination $destination
            if ((Hash $file.FullName) -ne $before -or (Hash $destination) -ne $before) { throw "Native package changed while copying: $($file.FullName)" }
            $result.nativeInputs += ,[ordered]@{ source = $file.FullName; path = $destination; sha256 = $before }
        }
    }
    $dobby = Join-Path $output 'native-inputs/dobby.dll'
    Copy-Item -LiteralPath $DobbyPath -Destination $dobby
    $result.nativeInputs += ,[ordered]@{ source = $DobbyPath; path = $dobby; sha256 = Hash $DobbyPath }
    $getTime = Join-Path $output 'native-inputs/gettime-r4/RizToolbox.SingleReturn.Prototype.dll'
    $sourceHelper = Join-Path $output 'native-inputs/source-native/RizToolbox.PlaybackSourcePrototype.dll'
    Json $result.nativeInputs (Join-Path $output 'native-inputs.json')
    $config = Join-Path $output 'NuGet.Config'
    [IO.File]::WriteAllText($config, '<configuration><packageSources><clear/></packageSources></configuration>')
    $project = Join-Path $sourceRoot 'tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj'
    $buildLog = Join-Path $output 'build.log'
    & (Get-RizDotnetPath) build $project -c Release -m:1 --configfile $config -p:UseSharedCompilation=false --nologo -v:minimal 2>&1 |
        Out-File -LiteralPath $buildLog
    $result.buildExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 20; throw 'Actual frozen joint profile build failed.' }
    $binaryDirectory = Join-Path (Split-Path -Parent $project) 'bin/Release/net8.0'
    $assembly = Join-Path $binaryDirectory 'RizToolbox.Gameplay.Tests.dll'
    if ($FoundationHelperDirectory) {
        $helper = Join-Path $output 'native-inputs/foundation-native/RizToolbox.JudgeContext.dll'
        Copy-Item -LiteralPath $helper -Destination (Join-Path $binaryDirectory 'RizToolbox.JudgeContext.dll')
        if ((Hash $helper) -ne (Hash (Join-Path $binaryDirectory 'RizToolbox.JudgeContext.dll'))) { throw 'Copied actual foundation helper differs.' }
    }
    foreach ($name in @('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime', 'RizToolbox.Adapter', 'RizToolbox.Gameplay.Tests')) {
        $result.managedSymbols += ,(ManagedSymbolProof (Join-Path $binaryDirectory ($name + '.dll')) $sourceRoot $frozenHashes)
    }
    Json $result.managedSymbols (Join-Path $output 'managed-symbol-source-proof.json')
    $modes = @('normal', 'pitch-tail', 'body', 'consumer')
    if ($FoundationHelperDirectory) { $modes += @('foundation-normal', 'foundation-outside', 'foundation-unwind') }
    foreach ($mode in $modes) {
        $image = Join-Path $output ('images/' + $mode + '/GameAssembly.dll')
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $image) -Force
        Copy-Item -LiteralPath $GameAssemblyPath -Destination $image
        if ((Hash $image) -ne $fixedImageHash) { throw 'The isolated fixed original image copy differs.' }
        $result.images += ,[ordered]@{ mode = $mode; path = $image; sha256 = $fixedImageHash }
        $log = Join-Path $output ($mode + '.log')
        & (Get-RizDotnetPath) $assembly --mapped-playback-stock-group-checks $image $dobby $getTime $sourceHelper $mode 2>&1 |
            Out-File -LiteralPath $log
        $actualExit = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $output ($mode + '.exit.txt')), [string]$actualExit)
        $row = [ordered]@{ mode = $mode; actualExitCode = $actualExit; checks = 0; log = [ordered]@{ path = $log; sha256 = Hash $log } }
        $result.modes += ,$row
        if ($actualExit -ne 0) { Get-Content -LiteralPath $log -Tail 12; throw "Actual fixed joint $mode process failed." }
        $lines = @([IO.File]::ReadAllLines($log) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $actual = $lines[-1] | ConvertFrom-Json
        # The common prefix checks eleven complete hook bodies, including the
        # Progress player leaf and AudioSource time setter.
        $expectedChecks = switch ($mode) { 'normal' { 30 } 'foundation-normal' { 34 } 'foundation-outside' { 37 } 'foundation-unwind' { 37 } default { 34 } }
        if ($actual.passed -ne $true -or $actual.kind -ne 'mapped-playback-stock-joint-profile' -or $actual.mode -cne $mode -or
            $actual.checks -ne $expectedChecks -or
            $actual.actualDobbyPrepareCount -ne 11 -or $actual.actualDobbyCommitCount -ne 11 -or $actual.physicalMemberCount -ne 11 -or
            $actual.initialRevision -ne 1 -or $actual.commitRevision -ne 2 -or $actual.restoredRevision -ne 3 -or
            $actual.nativeAuthorityActiveAfterTermination -ne $false -or $actual.groupActiveAfterTermination -ne $false -or
            $actual.getTimeActiveAfterTermination -ne $false -or $actual.pitchActiveAfterTermination -ne $false -or
            $actual.getTimeOriginalCalls -ne 0 -or $actual.sourceEntered -ne 0 -or $actual.sourceExited -ne 0 -or
            $actual.sourceBefore -ne 0 -or $actual.sourceAfter -ne 0 -or $actual.metadataSynthetic -ne $true -or $actual.syntheticGraph -ne $true -or
            $actual.gameCodeExecuted -ne $false -or $actual.dllMainExecuted -ne $false -or $actual.gameLaunched -ne $false -or
            $actual.productionAdmission -ne $false) { throw 'The actual fixed joint result differs from the required authority and zero-game-execution checks.' }
        if ($mode.StartsWith('foundation-', [StringComparison]::Ordinal) -and
            ($actual.foundationEpoch -ne 1 -or $actual.foundationOriginalCount -ne 28 -or $actual.foundationWriteCount -ne 64)) {
            throw 'The combined mapped source group lost its actual private FirstInstall 28-original/64-write member.'
        }
        $row.checks = $actual.checks; $row.actual = $actual; $result.checks += $actual.checks
        Write-Output "$mode`: $($row.checks) actual fixed joint checks passed."
    }
    foreach ($row in @($result.sources) + @($result.nativeInputs) + @($result.images) + @($result.sourceProof) + @($result.nativeProof) + @($result.script)) {
        if ((Hash $row.path) -ne $row.sha256) { throw "A declared joint input changed: $($row.path)" }
    }
    foreach ($row in $result.managedSymbols) {
        foreach ($file in @($row.assembly) + @($row.pdb) + @($row.documents)) {
            if ((Hash $file.path) -ne $file.sha256) { throw "An actual PE/PDB/source artifact changed: $($file.path)" }
        }
    }
    if ((Hash $GameAssemblyPath) -ne $fixedImageHash) { throw 'The original game image changed during read-only evidence collection.' }
    Json $result.sources (Join-Path $output 'source-after.json')
    $result.frozenInputsUnchanged = $true
    $result.passed = $true
} catch { $result.failure = $_.Exception.Message; throw }
finally {
    $artifactFiles = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'test-result.json')
    $binaryDirectory = Join-Path $sourceRoot 'tests/RizToolbox.Gameplay.Tests/bin/Release/net8.0'
    if (Test-Path -LiteralPath $binaryDirectory) { $artifactFiles += @(Get-ChildItem -LiteralPath $binaryDirectory -File -Recurse) }
    $result.artifacts = @($artifactFiles | ForEach-Object { [ordered]@{ path = $_.FullName; sha256 = Hash $_.FullName; bytes = $_.Length } })
    Json $result (Join-Path $output 'test-result.json')
}
Write-Output "Current fixed stock joint profile: $($result.modes.Count) processes/$($result.checks) checks; no game methods or DllMain."
