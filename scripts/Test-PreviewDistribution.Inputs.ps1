#requires -Version 7.2
Set-StrictMode -Version Latest

# Test inputs are opaque synthetic bytes. No assembly, fixture executable or
# package in this helper is built, loaded or executed.
function Assert-PreviewPathInside {
    param([string]$Root, [string]$Path, [switch]$AllowRoot)
    $absoluteRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    $absolutePath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
    $isRoot = $absolutePath.Equals($absoluteRoot, [StringComparison]::OrdinalIgnoreCase)
    if (-not ($AllowRoot -and $isRoot) -and
        -not $absolutePath.StartsWith($absoluteRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Preview fixture path escaped its root: $absolutePath"
    }
    for ($cursor = $absolutePath; $cursor; $cursor = Split-Path -Parent $cursor) {
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Ignore
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Preview fixtures cannot traverse linked paths: $cursor"
        }
    }
    return $absolutePath
}

function Write-PreviewInputFile([string]$Root, [string]$Path, [string]$Text) {
    $safePath = Assert-PreviewPathInside $Root $Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $safePath) -Force | Out-Null
    [IO.File]::WriteAllText($safePath, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-PreviewJson([string]$Root, [string]$Path, $Value) {
    Write-PreviewInputFile $Root $Path ($Value | ConvertTo-Json -Depth 12)
}

function Get-PreviewHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Remove-PreviewFixturePath([string]$Root, [string]$Path) {
    # Validate the resolved absolute target before any recursive deletion.
    # The root itself is never an allowed deletion target.
    $safePath = Assert-PreviewPathInside $Root $Path
    if (-not (Test-Path -LiteralPath $safePath)) { return }
    $item = Get-Item -LiteralPath $safePath -Force -ErrorAction Stop
    if ($item.PSIsContainer) {
        foreach ($entry in Get-ChildItem -LiteralPath $safePath -Recurse -Force -ErrorAction Stop) {
            $null = Assert-PreviewPathInside $safePath $entry.FullName
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing linked fixture cleanup.' }
        }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $safePath -Recurse -Force -ErrorAction Stop
    } else {
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $safePath -Force -ErrorAction Stop
    }
}

function Write-PreviewInputArchive([string]$Root, [string]$Path, [System.Collections.IDictionary]$Entries) {
    $safePath = Assert-PreviewPathInside $Root $Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $safePath) -Force | Out-Null
    $stream = [IO.FileStream]::new($safePath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = $null
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($name in $Entries.Keys) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $destination = $entry.Open()
            try {
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes([string]$Entries[$name])
                $destination.Write($bytes, 0, $bytes.Length)
            } finally { $destination.Dispose() }
        }
    } finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $stream.Dispose()
    }
}

function Get-PreviewExpectedRuntimePaths([string[]]$BasicPluginIds = @()) {
    # Independent test contract, rather than the packager's own path helper.
    @(
        'BepInEx/plugins/RizToolbox/RizToolbox.Sdk.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.Core.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.Runtime.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.Adapter.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.Hosting.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.Loader.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll'
        'BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll'
        'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'
        'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip'
        'BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt'
        '.doorstop_version'
        'winhttp.dll'
    )
    foreach ($id in $BasicPluginIds) {
        "BepInEx/RizToolbox/mods/$id/plugin.json"
        "BepInEx/RizToolbox/mods/$id/RizToolbox.BasicPlugins.dll"
    }
}

function Get-PreviewBasicPluginIds {
    'basic.song-scores', 'basic.theme', 'basic.quick-panel'
}

function Get-PreviewExampleIds {
    'example.color-high', 'example.color-low', 'example.events', 'example.panel', 'example.scores'
}

function Get-PreviewExpectedResources([string]$PackageKind) {
    if ($PackageKind -eq 'Runtime') { return }
    $prefix = if ($PackageKind -eq 'Author') { 'author/examples' } else { 'validation/proof' }
    foreach ($id in Get-PreviewExampleIds) {
        "$prefix/packages/$id/plugin.json"
        "$prefix/packages/$id/RizToolbox.Examples.dll"
    }
    if ($PackageKind -eq 'Author') {
        'author/sdk/RizToolbox.Sdk.1.4.0.nupkg'
        'author/sdk/sdk-manifest.json'
        'author/templates/riz-plugin/.template.config/template.json'
        'author/templates/riz-plugin/Plugin.cs'
        'author/templates/riz-plugin/README.md'
        'author/templates/riz-plugin/RizModProject.csproj'
        'author/templates/riz-plugin/plugin.json'
        'author/templates/riz-song/.template.config/template.json'
        'author/templates/riz-song/Plugin.cs'
        'author/templates/riz-song/README.md'
        'author/templates/riz-song/RizSongProject.csproj'
        'author/templates/riz-song/plugin.json'
        'author/templates/riz-song/assets/chart.json'
        'author/templates/riz-song/assets/cover.png'
        'author/templates/riz-song/assets/song.wav'
        'author/tools/tools.local-catalog/plugin.json'
        'author/tools/tools.local-catalog/RizToolbox.Examples.dll'
        'author/examples/source/RizToolbox.Examples.csproj'
        'author/examples/source/ColorPlugin.cs'
        'author/examples/source/PanelPlugin.cs'
    } else {
        'validation/fixtures/JudgeContext.Fixture.exe'
        'validation/fixtures/EngineNodeKernel.EntryFixture.exe'
        'validation/scripts/Collect-DistributionEvidence.ps1'
        'validation/scripts/PackageDistribution.Common.ps1'
        'validation/proof/runtime.developer.example.json'
    }
}

function New-PreviewFixture {
    param(
        [string]$FixtureRoot,
        [string]$RepositoryRoot,
        [string]$Name,
        [ValidateSet('Runtime','Author','Validation')][string]$PackageKind,
        [string[]]$BasicPluginIds = @()
    )
    if ($Name -cnotmatch '^[a-z0-9][a-z0-9-]{0,100}$') { throw 'Invalid preview fixture name.' }
    $root = Assert-PreviewPathInside $FixtureRoot (Join-Path $FixtureRoot $Name)
    if (Test-Path -LiteralPath $root) { throw "Preview fixture already exists: $root" }
    $repo = Join-Path $root 'repo'
    $game = Join-Path $root 'fake-game'
    New-Item -ItemType Directory -Path (Join-Path $repo 'scripts') -Force | Out-Null
    foreach ($scriptName in @('Install.ps1','Install.Common.ps1','Uninstall.ps1','PackageDistribution.Common.ps1','Collect-DistributionEvidence.ps1')) {
        $source = Join-Path $RepositoryRoot "scripts/$scriptName"
        $target = Assert-PreviewPathInside $root (Join-Path $repo "scripts/$scriptName")
        Microsoft.PowerShell.Management\Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
    }
    Write-PreviewInputFile $root (Join-Path $repo 'scripts/Prepare-Dependencies.ps1') '# Synthetic dependency cache is already seeded. No downloads or builds.'
    Write-PreviewInputFile $root (Join-Path $game 'Rizline.exe') 'Synthetic executable marker; never launched.'
    Write-PreviewInputFile $root (Join-Path $game 'GameAssembly.dll') 'Synthetic original game bytes; never loaded.'
    Write-PreviewInputFile $root (Join-Path $root 'outside-game/canary.txt') 'Keep these bytes outside the synthetic game directory.'
    Write-PreviewJson $root (Join-Path $repo 'dependencies.lock.json') @{
        supportedBuild = @{ gameAssemblySha256 = (Get-PreviewHash (Join-Path $game 'GameAssembly.dll')) }
    }
    [pscustomobject]@{
        Name = $Name; Root = $root; RepoRoot = $repo; Game = $game; PackageKind = $PackageKind
        NativeOutputDirectory = (Join-Path $root 'native/judge')
        DoorstopOutput = (Join-Path $root 'native/doorstop')
        EngineKernelOutput = (Join-Path $root 'native/engine')
        PlaybackRuntimeOutput = (Join-Path $root 'native/playback')
        SdkDirectory = (Join-Path $root 'sdk-feed')
        ArtifactRoot = (Join-Path $repo 'artifacts')
        InstallationManifest = (Join-Path $game 'BepInEx/RizToolbox/installation.json')
        OriginalGameHashes = @{
            'Rizline.exe' = (Get-PreviewHash (Join-Path $game 'Rizline.exe'))
            'GameAssembly.dll' = (Get-PreviewHash (Join-Path $game 'GameAssembly.dll'))
        }
        OutsideCanaryHash = (Get-PreviewHash (Join-Path $root 'outside-game/canary.txt'))
        BasicPluginIds = @($BasicPluginIds); Distribution = $null
    }
}

function Set-PreviewDistributionInputs($Fixture, [string]$Revision, [string]$SdkFeedVersion = '1.4.0') {
    foreach ($name in @('Sdk','Core','Runtime','Adapter','Hosting','Loader')) {
        $relative = "src/RizToolbox.$name/bin/Release/net6.0/RizToolbox.$name.dll"
        Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.RepoRoot $relative) "$Revision synthetic managed payload $name"
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.RepoRoot 'src/RizToolbox.Sdk/bin/Release/net6.0/RizToolbox.Sdk.pdb') 'Unrelated debug symbols must not be distributed.'
    Write-PreviewJson $Fixture.Root (Join-Path $Fixture.RepoRoot 'src/RizToolbox.Adapter/profiles/steam-25977441.json') @{
        schemaVersion = 1; id = 'synthetic-offline-profile'; revision = $Revision
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.NativeOutputDirectory 'RizToolbox.JudgeContext.dll') "$Revision synthetic JudgeContext payload"
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.EngineKernelOutput 'RizToolbox.EngineNodeKernel.dll') "$Revision synthetic EngineNodeKernel payload"
    foreach ($name in @('RizToolbox.SingleReturn.Prototype.dll','RizToolbox.PlaybackSourcePrototype.dll')) {
        Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.PlaybackRuntimeOutput $name) "$Revision synthetic playback payload $name"
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.DoorstopOutput 'winhttp.dll') "$Revision synthetic Doorstop payload"
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.DoorstopOutput 'UnityDoorstop.LGPL-2.1.txt') "$Revision synthetic corresponding license fixture"
    Write-PreviewInputArchive $Fixture.Root (Join-Path $Fixture.DoorstopOutput 'doorstop-corresponding-source.zip') ([ordered]@{
        'SOURCE-FIXTURE.txt' = "$Revision synthetic corresponding source fixture"
    })
    foreach ($relative in @('BepInEx/core/dobby.dll','dotnet/runtime.dll','dotnet/host/hostfxr.dll')) {
        Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.RepoRoot ".tools/bepinex-789/$relative") "$Revision synthetic dependency $relative"
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.RepoRoot '.tools/bepinex-789/doorstop_config.ini') "[General]`ntarget_assembly = original`nfixture = $Revision`n"
    foreach ($id in Get-PreviewBasicPluginIds) {
        Write-PreviewJson $Fixture.Root (Join-Path $Fixture.RepoRoot "examples/basic-packages/$id/plugin.json") @{
            id = $id; version = '0.3.0'; revision = $Revision
            assembly = 'RizToolbox.BasicPlugins.dll'; entryType = 'Synthetic.BasicPlugin'
        }
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.RepoRoot 'examples/RizToolbox.BasicPlugins/bin/Release/net6.0/RizToolbox.BasicPlugins.dll') "$Revision synthetic ordinary plugin payload; never loaded"
    if ($Fixture.PackageKind -eq 'Runtime') { return }
    foreach ($id in Get-PreviewExampleIds) {
        Write-PreviewJson $Fixture.Root (Join-Path $Fixture.RepoRoot "examples/packages/$id/plugin.json") @{
            id = $id; version = '0.3.0'; revision = $Revision; assembly = 'RizToolbox.Examples.dll'; entryType = 'Synthetic.Example'; minSdk = '1.3.0'
        }
    }
    $examples = Join-Path $Fixture.RepoRoot 'examples/RizToolbox.Examples'
    Write-PreviewInputFile $Fixture.Root (Join-Path $examples 'bin/Release/net6.0/RizToolbox.Examples.dll') "$Revision synthetic example payload"
    Write-PreviewInputFile $Fixture.Root (Join-Path $examples 'ColorPlugin.cs') "// $Revision synthetic example source`npublic sealed class ColorPlugin { }`n"
    Write-PreviewInputFile $Fixture.Root (Join-Path $examples 'PanelPlugin.cs') "// $Revision synthetic example source`npublic sealed class PanelPlugin { }`n"
    Write-PreviewInputFile $Fixture.Root (Join-Path $examples 'internal/DoNotShip.cs') 'Nested unrelated source is outside the Author source contract.'
    Write-PreviewInputFile $Fixture.Root (Join-Path $examples 'RizToolbox.Examples.csproj') @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/RizToolbox.Sdk/RizToolbox.Sdk.csproj" /></ItemGroup>
</Project>
'@
    if ($Fixture.PackageKind -eq 'Validation') {
        Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.NativeOutputDirectory 'JudgeContext.Fixture.exe') "$Revision synthetic JudgeContext fixture; never executed"
        Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.EngineKernelOutput 'EngineNodeKernel.EntryFixture.exe') "$Revision synthetic EngineNodeKernel fixture; never executed"
        return
    }
    $sdkPackageName = "RizToolbox.Sdk.$SdkFeedVersion.nupkg"
    Write-PreviewJson $Fixture.Root (Join-Path $Fixture.RepoRoot 'examples/packages/tools.local-catalog/plugin.json') @{
        id = 'tools.local-catalog'; entryAssembly = 'RizToolbox.Examples.dll'; entryType = 'Synthetic.LocalCatalogBrowser'; revision = $Revision
    }
    $sdkPackage = Join-Path $Fixture.SdkDirectory $sdkPackageName
    Write-PreviewInputArchive $Fixture.Root $sdkPackage ([ordered]@{
        'RizToolbox.Sdk.nuspec' = "<package><metadata><id>RizToolbox.Sdk</id><version>$SdkFeedVersion</version><authors>Synthetic</authors><description>Offline input fixture</description></metadata></package>"
        'ref/net6.0/RizToolbox.Sdk.dll' = "$Revision synthetic SDK reference bytes; never loaded"
    })
    Write-PreviewJson $Fixture.Root (Join-Path $Fixture.SdkDirectory 'sdk-manifest.json') @{
        schemaVersion = 1; sdkContractVersion = $SdkFeedVersion; assemblyVersion = '1.0.0.0'
        package = $sdkPackageName; sha256 = (Get-PreviewHash $sdkPackage)
    }
    $template = Join-Path $Fixture.RepoRoot 'templates/riz-plugin'
    Write-PreviewJson $Fixture.Root (Join-Path $template '.template.config/template.json') @{
        identity = 'Synthetic.RizPlugin'; name = 'Synthetic plugin fixture'; shortName = 'riz-fixture'; sourceName = 'RizModProject'
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $template 'Plugin.cs') "// $Revision synthetic template`npublic sealed class Plugin { }`n"
    Write-PreviewInputFile $Fixture.Root (Join-Path $template 'README.md') 'Synthetic template instructions.'
    Write-PreviewInputFile $Fixture.Root (Join-Path $template 'RizModProject.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="RizToolbox.Sdk" Version="1.4.0" IncludeAssets="compile" PrivateAssets="all" /></ItemGroup></Project>'
    Write-PreviewJson $Fixture.Root (Join-Path $template 'plugin.json') @{ id = 'synthetic.template'; version = '0.1.0'; assembly = 'RizModProject.dll'; minSdk = '1.4.0' }
    Write-PreviewInputFile $Fixture.Root (Join-Path $template 'DoNotShip.bin') 'Unlisted template input must not become a resource.'
    $songTemplate = Join-Path $Fixture.RepoRoot 'templates/riz-song'
    foreach ($relative in '.template.config/template.json','Plugin.cs','README.md','plugin.json') {
        Write-PreviewInputFile $Fixture.Root (Join-Path $songTemplate $relative) (Get-Content -LiteralPath (Join-Path $template $relative) -Raw)
    }
    Write-PreviewInputFile $Fixture.Root (Join-Path $songTemplate 'RizSongProject.csproj') (Get-Content -LiteralPath (Join-Path $template 'RizModProject.csproj') -Raw)
    foreach ($name in 'chart.json','song.wav','cover.png','DoNotShip.bin') {
        Write-PreviewInputFile $Fixture.Root (Join-Path $songTemplate "assets/$name") "$Revision fixture bytes for $name"
    }
}

function Get-PreviewTreeSnapshot([string]$Root, [switch]$IgnoreInstallerManagement) {
    $hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $Root)) { return ,$hashes }
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction Stop) {
        $null = Assert-PreviewPathInside $Root $file.FullName
        if ($file.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\','/')
        if ($IgnoreInstallerManagement -and
            ($relative -in @('.riz_toolbox.install.lock','BepInEx/RizToolbox/installation.json') -or
                $relative.StartsWith('BepInEx/RizToolbox/install-recovery/', [StringComparison]::OrdinalIgnoreCase))) { continue }
        $hashes.Add($relative, (Get-PreviewHash $file.FullName))
    }
    return ,$hashes
}

function Set-PreviewUserFiles($Fixture) {
    $contents = [ordered]@{
        'BepInEx/RizToolbox/settings.json' = '{"enableExamples":false,"runSelfValidation":false,"custom":"preserve settings bytes"}'
        'BepInEx/RizToolbox/runtime.json' = '{"schemaVersion":1,"mode":"Developer","logLevel":"Warning","fullAuditEachTick":false,"enableProofPlugins":false,"custom":"preserve runtime bytes"}'
        'BepInEx/RizToolbox/activation.json' = '{"enabled":{"user.fixture":true,"example.panel":false}}'
        'BepInEx/RizToolbox/plugins/user.fixture/preferences.json' = '{"schemaVersion":1,"language":"zh-CN","custom":"preserve plugin preferences"}'
        'BepInEx/RizToolbox/mods/user.fixture/plugin.json' = '{"id":"user.fixture","version":"1.0.0","assembly":"UserFixture.dll"}'
        'BepInEx/RizToolbox/mods/user.fixture/UserFixture.dll' = 'User-controlled plugin bytes; never loaded.'
        'BepInEx/RizToolbox/mods/example.panel/plugin.json' = '{"id":"example.panel","custom":"previous user-controlled proof package"}'
        'BepInEx/RizToolbox/mods/example.panel/RizToolbox.Examples.dll' = 'Previous user-controlled example bytes; never loaded.'
        'BepInEx/plugins/UserFixture/UserFixture.dll' = 'Foreign plugin directory must be preserved.'
        'BepInEx/RizToolbox/logs/user-session/events-000001.jsonl' = '{"sequence":1,"message":"preserve generated user logs"}'
        'BepInEx/RizToolbox/diagnostics/user-session/session.json' = '{"schemaVersion":1,"sessionId":"user-session"}'
        'BepInEx/RizToolbox/diagnostics/user-session/events-000001.jsonl' = '{"sequence":1,"message":"preserve diagnostic bytes"}'
        'BepInEx/RizToolbox/toolbox.log' = 'Retained user text log.'
    }
    $hashes = @{}
    foreach ($relative in $contents.Keys) {
        $path = Join-Path $Fixture.Game $relative
        Write-PreviewInputFile $Fixture.Root $path $contents[$relative]
        $hashes[$relative] = Get-PreviewHash $path
    }
    return $hashes
}
