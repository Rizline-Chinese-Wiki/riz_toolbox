#requires -Version 7.2
Set-StrictMode -Version Latest

function Get-DistributionBasicPluginIds {
    'basic.song-scores','basic.theme','basic.quick-panel'
}

function Assert-DistributionBasicPluginIds([string[]]$BasicPluginIds) {
    if ($null -eq $BasicPluginIds -or $BasicPluginIds.Count -gt 3 -or
        @($BasicPluginIds | Sort-Object -Unique).Count -ne $BasicPluginIds.Count) {
        throw 'Select at most one copy of each supported basic plugin.'
    }
    foreach ($id in $BasicPluginIds) {
        if ($id -cnotin @(Get-DistributionBasicPluginIds)) { throw "Unknown basic plugin: $id" }
    }
}

function Get-RuntimeDistributionPaths([string[]]$BasicPluginIds = @()) {
    Assert-DistributionBasicPluginIds $BasicPluginIds
    foreach ($name in @('Sdk','Core','Runtime','Adapter','Hosting','Loader','JudgeContext','EngineNodeKernel','SingleReturn.Prototype','PlaybackSourcePrototype')) {
        "BepInEx/plugins/RizToolbox/RizToolbox.$name.dll"
    }
    'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'
    'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip'
    'BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt'
    '.doorstop_version'
    'winhttp.dll'
    foreach ($id in $BasicPluginIds) {
        "BepInEx/RizToolbox/mods/$id/plugin.json"
        "BepInEx/RizToolbox/mods/$id/RizToolbox.BasicPlugins.dll"
    }
}

function Get-DistributionExampleIds {
    'example.color-high','example.color-low','example.events','example.panel','example.scores'
}

function Get-RequiredDistributionResources([string]$PackageKind, [string]$SdkPackageName) {
    if ($PackageKind -eq 'Runtime') { return }
    $resourceRoot = if ($PackageKind -eq 'Author') { 'author/examples' } else { 'validation/proof' }
    foreach ($id in Get-DistributionExampleIds) {
        "$resourceRoot/packages/$id/plugin.json"
        "$resourceRoot/packages/$id/RizToolbox.Examples.dll"
    }
    if ($PackageKind -eq 'Author') {
        if ($SdkPackageName -cnotmatch '^RizToolbox\.Sdk\.[0-9]+\.[0-9]+\.[0-9]+\.nupkg$') {
            throw 'An exact SDK package name is required for author resources.'
        }
        "author/sdk/$SdkPackageName"
        'author/sdk/sdk-manifest.json'
        'author/templates/riz-plugin/.template.config/template.json'
        'author/templates/riz-plugin/Plugin.cs'
        'author/templates/riz-plugin/README.md'
        'author/templates/riz-plugin/RizModProject.csproj'
        'author/templates/riz-plugin/plugin.json'
        'author/examples/source/RizToolbox.Examples.csproj'
    } else {
        'validation/fixtures/JudgeContext.Fixture.exe'
        'validation/fixtures/EngineNodeKernel.EntryFixture.exe'
        'validation/scripts/Collect-DistributionEvidence.ps1'
        'validation/scripts/PackageDistribution.Common.ps1'
        'validation/proof/runtime.developer.example.json'
    }
}

function Get-DistributionHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Assert-DistributionPathWithoutLinks([string]$Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = Split-Path -Parent $cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Ignore
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked distribution path: $cursor" }
    }
}

function Get-DistributionSdkPackage([string]$SdkDirectory) {
    $manifestPath = Join-Path $SdkDirectory 'sdk-manifest.json'
    Assert-DistributionPathWithoutLinks $manifestPath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Missing distribution input: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $contract = $manifest.sdkContractVersion
    if ($manifest.schemaVersion -ne 1 -or $contract -isnot [string] -or
        $contract -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $manifest.package -cne "RizToolbox.Sdk.$contract.nupkg" -or
        $manifest.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Invalid SDK feed manifest.'
    }
    $package = Join-Path $SdkDirectory $manifest.package
    Assert-DistributionPathWithoutLinks $package
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Missing distribution input: $package"
    }
    if ((Get-DistributionHash $package) -cne $manifest.sha256) {
        throw "Package hash mismatch: $package"
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $spec = @($zip.Entries | Where-Object FullName -CEQ 'RizToolbox.Sdk.nuspec')
        $reference = @($zip.Entries | Where-Object FullName -CEQ 'ref/net6.0/RizToolbox.Sdk.dll')
        if ($spec.Count -ne 1 -or $reference.Count -ne 1 -or $spec[0].Length -gt 64KB) {
            throw 'The SDK feed package does not contain its unique specification and compiler reference.'
        }
        $reader = [IO.StreamReader]::new($spec[0].Open())
        try { $metadata = ([xml]$reader.ReadToEnd()).package.metadata }
        finally { $reader.Dispose() }
        if ($metadata.id -cne 'RizToolbox.Sdk' -or $metadata.version -cne $contract) {
            throw 'The SDK package identity differs from its feed manifest.'
        }
        $stream = $reference[0].Open()
        try { $referenceSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
    [pscustomobject]@{
        Version=$contract; Name=$manifest.package; Path=$package; ManifestPath=$manifestPath
        Sha256=$manifest.sha256; AssemblyVersion=$manifest.assemblyVersion; ReferenceSha256=$referenceSha256
    }
}

function Assert-RuntimeDistribution([string]$PackageRoot, $Manifest, [string]$PackageKind) {
    if ($Manifest.schemaVersion -ne 3 -or $Manifest.packageKind -cne $PackageKind -or $Manifest.acceptance -cne 'preview/unverified' -or
        $Manifest.developmentAssetsInstalled -isnot [bool] -or $Manifest.developmentAssetsInstalled -or
        $Manifest.proofEnabledByPackage -isnot [bool] -or $Manifest.proofEnabledByPackage) {
        throw 'Invalid package identity or development policy.'
    }
    Assert-DistributionPathWithoutLinks $PackageRoot
    $basicIds = $Manifest.basicPlugins
    if ($basicIds -isnot [System.Array] -or @($basicIds | Where-Object { $_ -isnot [string] }).Count) {
        throw 'The package must declare its explicitly selected basic plugins.'
    }
    Assert-DistributionBasicPluginIds $basicIds
    $expectedPaths = @(Get-RuntimeDistributionPaths $basicIds)
    $sdkPackageName = $null
    if ($PackageKind -ceq 'Author') {
        $sdkPaths = @($Manifest.resources | Where-Object path -CMatch '^author/sdk/RizToolbox\.Sdk\.[0-9]+\.[0-9]+\.[0-9]+\.nupkg$')
        if ($sdkPaths.Count -ne 1) { throw 'An author package must list one exact SDK package.' }
        $sdkPackageName = [IO.Path]::GetFileName($sdkPaths[0].path)
    }
    $requiredResources = @(Get-RequiredDistributionResources $PackageKind $sdkPackageName)
    $content = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Manifest.files) {
        if ($entry.path -isnot [string] -or $entry.path -cnotin $expectedPaths -or
            $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            -not $content.TryAdd($entry.path, $entry)) { throw 'Unexpected runtime package entry.' }
    }
    if ($content.Count -ne $expectedPaths.Count) { throw 'Missing runtime package entries.' }
    foreach ($entry in $Manifest.resources) {
        if ($entry.path -isnot [string] -or
            ($entry.path -cnotin $requiredResources -and
                ($PackageKind -ne 'Author' -or $entry.path -cnotmatch '^author/examples/source/[A-Za-z0-9_][A-Za-z0-9_.\-]*\.cs$')) -or
            $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            -not $content.TryAdd($entry.path, $entry)) { throw 'Unexpected development resource entry.' }
    }
    foreach ($relative in $requiredResources) {
        if (-not $content.ContainsKey($relative)) { throw "Missing development resource: $relative" }
    }
    $actualCount = 0
    foreach ($file in Get-ChildItem -LiteralPath $PackageRoot -Recurse -Force -ErrorAction Stop) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked package content is not supported.' }
        if ($file.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($PackageRoot, $file.FullName).Replace('\','/')
        if (-not $content.ContainsKey($relative) -or $content[$relative].path -cne $relative) { throw "Unlisted package file: $relative" }
        if ((Get-DistributionHash $file.FullName) -cne $content[$relative].sha256) { throw "Package hash mismatch: $relative" }
        $actualCount++
    }
    if ($actualCount -ne $content.Count) { throw 'Missing package content.' }
    if ($PackageKind -ceq 'Author') {
        $sdk = Get-DistributionSdkPackage (Join-Path $PackageRoot 'author/sdk')
        if ($sdk.Name -cne $sdkPackageName) { throw 'The author resource index differs from its SDK feed manifest.' }
    }
}

function New-RuntimeDistribution {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [ValidateSet('Runtime','Author','Validation')][string]$PackageKind = 'Runtime',
        [Parameter(Mandatory)][string]$NativeOutputDirectory,
        [Parameter(Mandatory)][string]$DoorstopOutput,
        [Parameter(Mandatory)][string]$EngineKernelOutput,
        [Parameter(Mandatory)][string]$PlaybackRuntimeOutput,
        [string]$OutputDirectory,
        [string]$SdkDirectory,
        [string[]]$BasicPluginIds = @()
    )
    $ErrorActionPreference = 'Stop'
    $PackageKind = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($PackageKind.ToLowerInvariant())
    Assert-DistributionBasicPluginIds $BasicPluginIds
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot 'artifacts' }
    if (-not $SdkDirectory) { $SdkDirectory = Join-Path $RepoRoot 'artifacts/sdk' }
    $artifactRoot = [IO.Path]::TrimEndingDirectorySeparator($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory))
    $publicationRoot = if ($PackageKind -eq 'Runtime') { $artifactRoot } else { Join-Path $artifactRoot $PackageKind.ToLowerInvariant() }
    $packageRoot = Join-Path $publicationRoot 'package'
    $manifestPath = Join-Path $publicationRoot 'package-manifest.json'
    $runId = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path $publicationRoot "package-stage-$runId"
    Assert-DistributionPathWithoutLinks $stage
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $files = [Collections.Generic.List[string]]::new()
    $resources = [Collections.Generic.List[string]]::new()
    function Copy-DistributionFile([string]$Source, [string]$Relative, [bool]$Resource = $false) {
        Assert-DistributionPathWithoutLinks $Source
        if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Missing distribution input: $Source" }
        $target = Join-Path $stage $Relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $Source -Destination $target
        if ($Resource) { $resources.Add($Relative) } else { $files.Add($Relative) }
    }
    Copy-DistributionFile (Join-Path $DoorstopOutput 'winhttp.dll') 'winhttp.dll'
    [IO.File]::WriteAllText((Join-Path $stage '.doorstop_version'), '4.5.0-riz-preinit.2', [Text.UTF8Encoding]::new($false))
    $files.Add('.doorstop_version')
    foreach ($name in @('doorstop-corresponding-source.zip','UnityDoorstop.LGPL-2.1.txt')) {
        Copy-DistributionFile (Join-Path $DoorstopOutput $name) "BepInEx/plugins/RizToolbox/licenses/$name"
    }
    foreach ($name in @('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader')) {
        Copy-DistributionFile (Join-Path $RepoRoot "src/$name/bin/Release/net6.0/$name.dll") "BepInEx/plugins/RizToolbox/$name.dll"
    }
    Copy-DistributionFile (Join-Path $NativeOutputDirectory 'RizToolbox.JudgeContext.dll') 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
    Copy-DistributionFile (Join-Path $EngineKernelOutput 'RizToolbox.EngineNodeKernel.dll') 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
    foreach ($name in @('RizToolbox.SingleReturn.Prototype.dll','RizToolbox.PlaybackSourcePrototype.dll')) {
        Copy-DistributionFile (Join-Path $PlaybackRuntimeOutput $name) "BepInEx/plugins/RizToolbox/$name"
    }
    Copy-DistributionFile (Join-Path $RepoRoot 'src/RizToolbox.Adapter/profiles/steam-25977441.json') 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'
    foreach ($id in $BasicPluginIds) {
        $relative = "BepInEx/RizToolbox/mods/$id"
        Copy-DistributionFile (Join-Path $RepoRoot "examples/basic-packages/$id/plugin.json") "$relative/plugin.json"
        Copy-DistributionFile (Join-Path $RepoRoot 'examples/RizToolbox.BasicPlugins/bin/Release/net6.0/RizToolbox.BasicPlugins.dll') "$relative/RizToolbox.BasicPlugins.dll"
    }
    if ($PackageKind -ne 'Runtime') {
        $resourceRoot = if ($PackageKind -eq 'Author') { 'author/examples' } else { 'validation/proof' }
        foreach ($id in Get-DistributionExampleIds) {
            $relative = "$resourceRoot/packages/$id"
            Copy-DistributionFile (Join-Path $RepoRoot "examples/packages/$id/plugin.json") "$relative/plugin.json" $true
            Copy-DistributionFile (Join-Path $RepoRoot 'examples/RizToolbox.Examples/bin/Release/net6.0/RizToolbox.Examples.dll') "$relative/RizToolbox.Examples.dll" $true
        }
        if ($PackageKind -eq 'Author') {
            $sdk = Get-DistributionSdkPackage $SdkDirectory
            foreach ($name in @($sdk.Name,'sdk-manifest.json')) {
                Copy-DistributionFile (Join-Path $SdkDirectory $name) "author/sdk/$name" $true
            }
            $templatePrefix = 'author/templates/riz-plugin/'
            foreach ($path in Get-RequiredDistributionResources 'Author' $sdk.Name | Where-Object { $_.StartsWith($templatePrefix, [StringComparison]::Ordinal) }) {
                $relative = $path.Substring($templatePrefix.Length)
                Copy-DistributionFile (Join-Path $RepoRoot "templates/riz-plugin/$relative") $path $true
            }
            $exampleSource = Join-Path $RepoRoot 'examples/RizToolbox.Examples'
            foreach ($file in Get-ChildItem -LiteralPath $exampleSource -Filter '*.cs' -File) {
                Copy-DistributionFile $file.FullName "author/examples/source/$($file.Name)" $true
            }
            # Package the same example source against the released SDK feed.
            $projectPath = 'author/examples/source/RizToolbox.Examples.csproj'
            $sourceProject = Join-Path $exampleSource 'RizToolbox.Examples.csproj'
            Assert-DistributionPathWithoutLinks $sourceProject
            $exampleProject = [xml](Get-Content -LiteralPath $sourceProject -Raw)
            $reference = $exampleProject.SelectSingleNode('/Project/ItemGroup/ProjectReference')
            if ($null -eq $reference -or $reference.Include -notmatch '[/\\]RizToolbox\.Sdk\.csproj$') { throw 'Unexpected example project SDK reference.' }
            $packageReference = $exampleProject.CreateElement('PackageReference')
            $packageReference.SetAttribute('Include', 'RizToolbox.Sdk')
            $packageReference.SetAttribute('Version', $sdk.Version)
            $packageReference.SetAttribute('IncludeAssets', 'compile')
            $packageReference.SetAttribute('PrivateAssets', 'all')
            $null = $reference.ParentNode.ReplaceChild($packageReference, $reference)
            $properties = $exampleProject.SelectSingleNode('/Project/PropertyGroup')
            foreach ($property in @(
                @{Name='Nullable';Value='enable'}, @{Name='ImplicitUsings';Value='enable'}, @{Name='LangVersion';Value='10.0'},
                @{Name='Version';Value='0.3.0'}, @{Name='CheckEolTargetFramework';Value='false'}
            )) {
                if ($properties.SelectSingleNode($property.Name)) { continue }
                $node = $exampleProject.CreateElement($property.Name)
                $node.InnerText = $property.Value
                $null = $properties.AppendChild($node)
            }
            $exampleProject.Save((Join-Path $stage $projectPath))
            $resources.Add($projectPath)
        } else {
            Copy-DistributionFile (Join-Path $NativeOutputDirectory 'JudgeContext.Fixture.exe') 'validation/fixtures/JudgeContext.Fixture.exe' $true
            Copy-DistributionFile (Join-Path $EngineKernelOutput 'EngineNodeKernel.EntryFixture.exe') 'validation/fixtures/EngineNodeKernel.EntryFixture.exe' $true
            foreach ($name in @('Collect-DistributionEvidence.ps1','PackageDistribution.Common.ps1')) {
                Copy-DistributionFile (Join-Path $RepoRoot "scripts/$name") "validation/scripts/$name" $true
            }
            $examplePath = 'validation/proof/runtime.developer.example.json'
            # Proof mode requires an explicit startup configuration.
            [IO.File]::WriteAllText((Join-Path $stage $examplePath), '{"schemaVersion":1,"mode":"Developer","logLevel":"Debug","fullAuditEachTick":true,"enableProofPlugins":true}', [Text.UTF8Encoding]::new($false))
            $resources.Add($examplePath)
        }
    }
    function Get-DistributionRows($Paths) {
        @($Paths | Sort-Object | ForEach-Object {
            [ordered]@{path=$_;sha256=(Get-DistributionHash (Join-Path $stage $_))}
        })
    }
    $manifest = [ordered]@{
        schemaVersion=3;version='0.3.0';packageKind=$PackageKind;acceptance='preview/unverified'
        basicPlugins=@($BasicPluginIds)
        loader='Doorstop modular compatibility host';files=@(Get-DistributionRows $files);resources=@(Get-DistributionRows $resources)
        developmentAssetsInstalled=$false;proofEnabledByPackage=$false
    }
    Assert-RuntimeDistribution $stage $manifest $PackageKind
    $temporaryManifest = Join-Path $publicationRoot "package-manifest-$runId.tmp"
    $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $temporaryManifest -Encoding utf8
    $lockPath = Join-Path $publicationRoot '.package-publication.lock'
    Assert-DistributionPathWithoutLinks $lockPath
    $publicationLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        Assert-DistributionPathWithoutLinks $packageRoot
        Assert-DistributionPathWithoutLinks $manifestPath
        $previousPackage = $null
        if ((Test-Path -LiteralPath $packageRoot) -or (Test-Path -LiteralPath $manifestPath)) {
            $history = Join-Path $artifactRoot ('package-history/' + $PackageKind.ToLowerInvariant() + "-$runId")
            Assert-DistributionPathWithoutLinks $history
            New-Item -ItemType Directory -Path $history | Out-Null
            if (Test-Path -LiteralPath $manifestPath) {
                Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $history 'package-manifest.json')
            }
            if (Test-Path -LiteralPath $packageRoot) {
                $previousPackage = Join-Path $history 'package'
                Move-Item -LiteralPath $packageRoot -Destination $previousPackage
            }
        }
        $published = $false
        try {
            Move-Item -LiteralPath $stage -Destination $packageRoot
            $published = $true
            [IO.File]::Move($temporaryManifest, $manifestPath, $true)
        } catch {
            if ($published) { Move-Item -LiteralPath $packageRoot -Destination $stage }
            if ($previousPackage) { Move-Item -LiteralPath $previousPackage -Destination $packageRoot }
            throw
        }
    } finally { $publicationLock.Dispose() }
    [pscustomobject]@{PackageKind=$PackageKind;PackageRoot=$packageRoot;ManifestPath=$manifestPath;Manifest=$manifest}
}
