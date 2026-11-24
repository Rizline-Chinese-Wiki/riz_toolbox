#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DistributionDirectory,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$')][string]$Label
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$deliveryRequest = [ordered]@{distribution=$DistributionDirectory;repository=$RepositoryRoot;output=$OutputDirectory;label=$Label}
$deliveryLibrary = Join-Path $PSScriptRoot 'Test-PreviewDelivery.ps1'
for ($cursor = [IO.Path]::GetFullPath($deliveryLibrary); $cursor; $cursor = Split-Path -Parent $cursor) {
    $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked delivery tool: $cursor" }
}
. $deliveryLibrary -LibraryOnly
$repository = Resolve-DeliveryPath $deliveryRequest.repository
$distribution = Resolve-DeliveryPath $deliveryRequest.distribution
$output = Resolve-DeliveryPath $deliveryRequest.output
$deliveryLabel = $deliveryRequest.label
Assert-DeliveryLabel $deliveryLabel
$parent = Get-DeliveryItem (Split-Path -Parent $output)
if ($null -eq $parent -or -not $parent.PSIsContainer) { throw 'OutputDirectory needs an existing parent directory.' }
if ($null -ne (Get-DeliveryItem $output)) { throw 'Use a fresh OutputDirectory; existing output is never replaced.' }
# The repository is a source of explicit files, never a recursively copied tree.
foreach ($source in @($distribution,(Resolve-DeliveryPath $PSScriptRoot)) +
    @('scripts','docs','.tools','examples/RizToolbox.BasicPlugins/configuration' | ForEach-Object { Get-DeliveryTarget $repository $_ })) {
    if ((Test-DeliveryWithin $source $output) -or (Test-DeliveryWithin $output $source)) { throw "Delivery output overlaps a source directory: $source" }
}
$helper = Get-DeliveryTarget $repository 'scripts/PackageDistribution.Common.ps1'
$helperIdentity = Read-DeliveryFile $helper
. $helper
$sourceManifest = Get-DeliveryTarget $distribution 'package-manifest.json'
$sourcePackage = Get-DeliveryTarget $distribution 'package'
$manifestFile = Read-DeliveryFile $sourceManifest -CaptureBytes
$manifest = ConvertFrom-DeliveryJson $manifestFile.contentBytes
$publication = Get-DeliveryPublicationRelative $manifest.packageKind
$null = @(Get-DeliveryTreeFiles $sourcePackage)
Assert-RuntimeDistribution $sourcePackage $manifest $manifest.packageKind

$plan = [Collections.Generic.List[object]]::new()
foreach ($relative in Get-DeliverySupportPaths) {
    $source = Get-DeliveryTarget $repository $relative
    $plan.Add([pscustomobject]@{source=$source;relative="delivery/$relative";identity=(Read-DeliveryFile $source)})
}
$plan.Add([pscustomobject]@{source=$sourceManifest;relative="delivery/$publication/package-manifest.json";identity=$manifestFile})
foreach ($entry in @($manifest.files) + @($manifest.resources)) {
    $source = Get-DeliveryTarget $sourcePackage $entry.path
    $identity = Read-DeliveryFile $source
    if ($identity.sha256 -cne $entry.sha256) { throw "Distribution changed after validation: $($entry.path)" }
    $plan.Add([pscustomobject]@{source=$source;relative="delivery/$publication/package/$($entry.path)";identity=$identity})
}
$lock = ConvertFrom-DeliveryJson (Read-DeliveryFile (Get-DeliveryTarget $repository 'dependencies.lock.json') -CaptureBytes).contentBytes
if ($lock.bepinex.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
    (Read-DeliveryFile (Get-DeliveryTarget $repository '.tools/bepinex-789.zip')).sha256 -cne $lock.bepinex.sha256) {
    throw 'A verified local .tools/bepinex-789.zip is required; this script does not download dependencies.'
}

function New-DeliveryParent([string]$Path) {
    if (-not (Test-DeliveryWithin $output $Path)) { throw 'Delivery write escaped output.' }
    Assert-DeliveryNoLinks $Path
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    Assert-DeliveryNoLinks $Path
}
function Write-DeliveryNewBytes([string]$Path, [byte[]]$Bytes) {
    New-DeliveryParent $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    Assert-DeliveryNoLinks $Path
}
function Write-DeliveryNewJson([string]$Path, $Value) {
    Write-DeliveryNewBytes $Path ([Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 16) + "`n"))
}
# No Force and no replacement. On failure partial output stays available without a completion receipt.
$null = New-Item -ItemType Directory -Path $output -ErrorAction Stop
Assert-DeliveryNoLinks $output
foreach ($row in $plan) {
    $target = Get-DeliveryTarget $output $row.relative
    New-DeliveryParent $target
    $sourceStream = [IO.File]::Open($row.source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        Assert-DeliveryNoLinks $row.source
        $targetStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $sourceStream.CopyTo($targetStream); $targetStream.Flush($true) } finally { $targetStream.Dispose() }
    } finally { $sourceStream.Dispose() }
    $copied = Read-DeliveryFile $target
    if ($copied.sha256 -cne $row.identity.sha256 -or $copied.bytes -ne $row.identity.bytes) { throw "Source changed while copying: $($row.source)" }
}
foreach ($mode in @('Standard','Developer')) {
    Write-DeliveryNewJson (Get-DeliveryTarget $output ('delivery/examples/runtime.' + $mode.ToLowerInvariant() + '.example.json')) ([ordered]@{
        schemaVersion=1;mode=$mode;logLevel=$(if ($mode -ceq 'Developer') { 'Debug' } else { 'Info' });
        fullAuditEachTick=($mode -ceq 'Developer');enableProofPlugins=$false
    })
}
$basicText = if (@($manifest.basicPlugins).Count) { @($manifest.basicPlugins) -join ', ' } else { 'none (basicPlugins=[])' }
$pluginCommand = ''
$pluginGuidance = 'Choose an installed plugin ID from the actual status.json plugins list when sending enable or disable.'
if (@($manifest.basicPlugins) -ccontains 'basic.theme') {
    $pluginCommand = "pwsh -NoProfile -File .\scripts\Send-HostCommand.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline' -Action disable -PluginId basic.theme"
    $pluginGuidance += ' The disable example below applies to the basic.theme selected by this package.'
}
$instructions = @'
# RizToolbox preview delivery

Status: `preview/unverified`. PackageKind: {PACKAGE_KIND}. Selected basic plugins: {BASIC_PLUGINS}.
This package has file/ZIP consistency checks. The three in-game plugin scenarios, visible recovery after disabling,
restart behavior and real-machine cost remain unverified. Speed/倍率, Seek and full B01–B19 acceptance remain incomplete.
Old proof results and the dependency lock's historical build status do not grant acceptance to these bytes.

Use PowerShell 7.2 or later. Extract the ZIP, open PowerShell in its `delivery` folder, close Rizline,
and review the supplied docs before manually installing into your own supported game directory:

```powershell
pwsh -NoProfile -File .\scripts\Install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline' -PackageKind {PACKAGE_KIND} -DistributionDirectory '.\{PUBLICATION}' -RuntimeMode Standard
```

The pinned dependency ZIP is bundled; the installer verifies and extracts it locally. Start Rizline yourself after installation.
Existing configuration and activation preferences are preserved. To request a plugin change, send one command and
wait for `BepInEx/RizToolbox/status.json` and `toolbox.log` to report its actual result; writing a request alone is not success:

{PLUGIN_GUIDANCE}

```powershell
pwsh -NoProfile -File .\scripts\Send-HostCommand.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline' -Action status
{PLUGIN_COMMAND}
```

See `docs/Basic-plugins.md` for song/scores, theme, F8/WAV controls and configuration paths.
Configuration samples are in `examples/RizToolbox.BasicPlugins/configuration/`; runtime mode samples are in `examples/`.
Supply your own supported WAV at this path relative to the game directory:

```text
BepInEx/RizToolbox/plugin-storage/storage/plugin-basic.quick-panel/data/audio/owned.wav
```

No WAV is bundled and no playback is automatic. Verify hearing playback, stopping it, scene changes and disabling it on the real machine.
Use `New-PreviewAcceptanceRecord.ps1` and `Test-PreviewAcceptanceRecord.ps1` for a fresh Runtime candidate and an explicit
session declaration; keep their output outside the payload. These records still report `preview/unverified`.
For removal, close the game and run `scripts/Uninstall.ps1 -GameDirectory <your-game-directory>`; modified files and generated data are preserved.

Author/Validation resources stay in the distribution and are not installed by the ordinary installer.
Build commands in the docs require the full source repository; this delivery contains prebuilt payloads and installation scripts.
Validation fixtures and proof require an explicit separate decision. Delivery creation/readback executes no payload programs, installer or game.
'@
$instructions = $instructions.Replace('{PACKAGE_KIND}', [string]$manifest.packageKind).Replace('{BASIC_PLUGINS}', $basicText).Replace('{PUBLICATION}', $publication.Replace('/', '\')).Replace('{PLUGIN_GUIDANCE}', $pluginGuidance).Replace('{PLUGIN_COMMAND}', $pluginCommand)
Write-DeliveryNewBytes (Get-DeliveryTarget $output 'delivery/DELIVERY.md') ([Text.UTF8Encoding]::new($false).GetBytes($instructions + "`n"))

$members = @(@(Get-DeliveryTreeFiles (Get-DeliveryTarget $output 'delivery')) | Sort-Object | ForEach-Object {
    $identity = Read-DeliveryFile $_
    [ordered]@{path=[IO.Path]::GetRelativePath($output, $_).Replace('\','/');sha256=$identity.sha256;bytes=$identity.bytes}
})
$memberManifest = [ordered]@{schemaVersion=1;kind='RizToolbox.PreviewDeliveryMembers';label=$deliveryLabel;packageKind=$manifest.packageKind;
    distributionRelativeDirectory=$publication;packageManifestSha256=$manifestFile.sha256;basicPlugins=@($manifest.basicPlugins);
    acceptance='preview/unverified';productionAdmission=$false;members=$members}
$memberPath = Get-DeliveryTarget $output "$deliveryLabel.members.json"
Write-DeliveryNewJson $memberPath $memberManifest
$archivePath = Get-DeliveryTarget $output "$deliveryLabel.zip"
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($row in $members) {
            $sourceStream = [IO.File]::Open((Get-DeliveryTarget $output $row.path), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try {
                $entryStream = $zip.CreateEntry($row.path, [IO.Compression.CompressionLevel]::Optimal).Open()
                try { $sourceStream.CopyTo($entryStream) } finally { $entryStream.Dispose() }
            } finally { $sourceStream.Dispose() }
        }
    } finally { $zip.Dispose() }
    $archiveStream.Flush($true)
} finally { $archiveStream.Dispose() }
$savedMembers = Read-DeliveryFile $memberPath -CaptureBytes
$verified = Assert-DeliveryContent $output $repository $deliveryLabel (ConvertFrom-DeliveryJson $savedMembers.contentBytes)
# Seal only after every original selected source, not an old evidence bundle, still matches.
Assert-RuntimeDistribution $sourcePackage $manifest $manifest.packageKind
foreach ($row in $plan) {
    $current = Read-DeliveryFile $row.source
    if ($current.sha256 -cne $row.identity.sha256 -or $current.bytes -ne $row.identity.bytes -or
        $current.createdUtc -cne $row.identity.createdUtc -or $current.lastWriteUtc -cne $row.identity.lastWriteUtc) {
        throw "Source changed before delivery completion: $($row.source)"
    }
}
if ((Read-DeliveryFile $helper).sha256 -cne $helperIdentity.sha256 -or
    (Read-DeliveryFile $memberPath).sha256 -cne $savedMembers.sha256) { throw 'Delivery tools or member manifest changed before completion.' }
$receipt = [ordered]@{schemaVersion=1;kind='RizToolbox.PreviewDeliveryReceipt';label=$deliveryLabel;createdUtc=[DateTimeOffset]::UtcNow.ToString('o');
    packageKind=$verified.packageKind;packageManifestSha256=$verified.packageManifestSha256;membersManifestSha256=$savedMembers.sha256;
    archiveSha256=$verified.archiveSha256;archiveBytes=$verified.archiveBytes;memberCount=$verified.memberCount;
    readbackStatus='all-members-hash-matched';sourceConsistency='matched-at-pack-finish';acceptance='preview/unverified';
    payloadProgramsExecuted=$false;installExecuted=$false;gameExecuted=$false;realMachineAcceptanceVerified=$false;productionAdmission=$false}
$receiptPath = Get-DeliveryTarget $output "$deliveryLabel.receipt.json"
$receiptBytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $receipt -Depth 16) + "`n")
Write-DeliveryNewBytes $receiptPath $receiptBytes
if ((Read-DeliveryFile $receiptPath).sha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($receiptBytes)).ToLowerInvariant()) {
    throw 'Saved receipt differs from completed readback.'
}
[pscustomobject]@{DeliveryDirectory=(Get-DeliveryTarget $output 'delivery');ArchivePath=$archivePath;MembersManifestPath=$memberPath;ReceiptPath=$receiptPath;
    PackageKind=$verified.packageKind;MemberCount=$verified.memberCount;Acceptance='preview/unverified';RealMachineAcceptanceVerified=$false}
