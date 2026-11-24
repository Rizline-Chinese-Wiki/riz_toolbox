#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$RepositoryRoot,
    [string]$Label,
    [switch]$LibraryOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Shared with New-PreviewDelivery.ps1. Loading the library only defines functions.
function Get-DeliveryItem([string]$Path) {
    try { Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch {
        if ($_.CategoryInfo.Category -ne [Management.Automation.ErrorCategory]::ObjectNotFound) { throw }
        return $null
    }
}

function Assert-DeliveryNoLinks([string]$Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = Split-Path -Parent $cursor) {
        $item = Get-DeliveryItem $cursor
        if ($null -eq $item) { continue }
        $link = $item.PSObject.Properties['LinkType']
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            ($null -ne $link -and -not [string]::IsNullOrEmpty([string]$link.Value))) {
            throw "Linked delivery path: $cursor"
        }
    }
}

function Assert-DeliveryRelative([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or
        $Path -match '[\\:\x00-\x1f*?"<>|\[\]]') { throw "Unsafe delivery member: $Path" }
    foreach ($part in $Path.Split('/')) {
        if (-not $part -or $part -in @('.', '..') -or $part -match '[. ]$' -or
            $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw "Unsafe delivery member segment: $Path"
        }
    }
}

function Resolve-DeliveryPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\x00-\x1f*?"<>|\[\]]' -or
        $Path.StartsWith('\\?\') -or $Path.StartsWith('\\.\') -or
        ($Path -replace '^[A-Za-z]:', '') -match ':') { throw 'Use an ordinary literal filesystem path.' }
    foreach ($part in $Path.Replace('\', '/').Split('/', [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($part -in @('.', '..') -or $part -match '[. ]$' -or
            $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw "Unsafe path segment: $part" }
    }
    [Management.Automation.ProviderInfo]$provider = $null
    [Management.Automation.PSDriveInfo]$drive = $null
    $native = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path, [ref]$provider, [ref]$drive)
    if ($provider.Name -cne 'FileSystem') { throw 'Only filesystem paths are supported.' }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($native))
    Assert-DeliveryNoLinks $full
    return $full
}

function Test-DeliveryWithin([string]$Root, [string]$Path) {
    $prefix = if ([IO.Path]::EndsInDirectorySeparator($Root)) { $Root } else { $Root + [IO.Path]::DirectorySeparatorChar }
    return $Root.Equals($Path, [StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-DeliveryTarget([string]$Root, [string]$Relative) {
    Assert-DeliveryRelative $Relative
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not (Test-DeliveryWithin $Root $path) -or $path.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Delivery path escaped its root.' }
    Assert-DeliveryNoLinks $path
    return $path
}

function Assert-DeliveryLabel([string]$Value) {
    if ($Value -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$' -or
        $Value -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') { throw 'Label must be 1–80 safe letters, digits, underscores or hyphens.' }
}

function Get-DeliveryTreeFiles([string]$Root) {
    Assert-DeliveryNoLinks $Root
    $rootItem = Get-DeliveryItem $Root
    if ($null -eq $rootItem -or -not $rootItem.PSIsContainer) { throw "Missing delivery directory: $Root" }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count) {
        $directory = $pending.Pop()
        Assert-DeliveryNoLinks $directory
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop) {
            Assert-DeliveryNoLinks $item.FullName
            if (-not (Test-DeliveryWithin $Root $item.FullName)) { throw 'Delivery tree escaped its root.' }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $item.FullName }
        }
    }
}

function Read-DeliveryFile([string]$Path, [switch]$CaptureBytes) {
    Assert-DeliveryNoLinks $Path
    $item = Get-DeliveryItem $Path
    if ($null -eq $item -or $item.PSIsContainer) { throw "Missing delivery file: $Path" }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $item.Refresh()
        $size = $stream.Length
        $created = $item.CreationTimeUtc.ToString('o')
        $written = $item.LastWriteTimeUtc.ToString('o')
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
        $bytes = $null
        if ($CaptureBytes) {
            if ($size -gt 16MB) { throw 'Delivery JSON exceeds 16 MiB.' }
            $stream.Position = 0
            $buffer = [IO.MemoryStream]::new()
            try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $buffer.Dispose() }
            if ($bytes.Length -ne $size -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $hash) {
                throw "Delivery file changed while reading: $Path"
            }
        }
        $item.Refresh()
        Assert-DeliveryNoLinks $Path
        if ($stream.Length -ne $size -or $item.Length -ne $size -or $item.CreationTimeUtc.ToString('o') -cne $created -or
            $item.LastWriteTimeUtc.ToString('o') -cne $written) { throw "Delivery identity changed: $Path" }
        return [pscustomobject]@{path=$Path;sha256=$hash;bytes=$size;createdUtc=$created;lastWriteUtc=$written;contentBytes=$bytes}
    } finally { $stream.Dispose() }
}

function Assert-DeliveryJsonKeys($Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw "Duplicate delivery JSON key: $($property.Name)" }
            Assert-DeliveryJsonKeys $property.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) { Assert-DeliveryJsonKeys $child }
    }
}

function ConvertFrom-DeliveryJson([byte[]]$Bytes) {
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes).TrimStart([char]0xfeff)
    $document = [Text.Json.JsonDocument]::Parse($json)
    try { Assert-DeliveryJsonKeys $document.RootElement } finally { $document.Dispose() }
    $options = @{InputObject=$json;AsHashtable=$true;Depth=64}
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $options.DateKind = 'String' }
    return (ConvertFrom-Json @options)
}

function Get-DeliverySupportPaths {
    'dependencies.lock.json', '.tools/bepinex-789.zip'
    foreach ($name in @('Install.ps1','Install.Common.ps1','Uninstall.ps1','Prepare-Dependencies.ps1',
        'PackageDistribution.Common.ps1','Send-HostCommand.ps1','Export-Diagnostics.ps1',
        'New-PreviewAcceptanceRecord.ps1','Test-PreviewAcceptanceRecord.ps1','Collect-DistributionEvidence.ps1')) { "scripts/$name" }
    foreach ($name in @('Basic-plugins.md','Runtime-modes.md','Host-control.md','Diagnostics.md')) { "docs/$name" }
    foreach ($name in @('song-scores','theme','quick-panel')) { "examples/RizToolbox.BasicPlugins/configuration/$name.example.json" }
}

function Get-DeliveryPublicationRelative([string]$PackageKind) {
    if ($PackageKind -cnotin @('Runtime','Author','Validation')) { throw 'Unsupported delivery PackageKind.' }
    if ($PackageKind -ceq 'Runtime') { return 'artifacts' }
    return ('artifacts/' + $PackageKind.ToLowerInvariant())
}

function Assert-DeliveryContent([string]$Output, [string]$Repository, [string]$DeliveryLabel, $Members) {
    Assert-DeliveryLabel $DeliveryLabel
    if ($Members.schemaVersion -ne 1 -or $Members.kind -cne 'RizToolbox.PreviewDeliveryMembers' -or
        $Members.label -cne $DeliveryLabel -or $Members.acceptance -cne 'preview/unverified' -or
        $Members.productionAdmission -isnot [bool] -or $Members.productionAdmission -or $Members.members -isnot [array]) { throw 'Invalid delivery member manifest.' }
    $delivery = Get-DeliveryTarget $Output 'delivery'
    $publication = Get-DeliveryPublicationRelative $Members.packageKind
    if ($Members.distributionRelativeDirectory -cne $publication) { throw 'Wrong delivery package layout.' }
    $manifestPath = Get-DeliveryTarget $delivery "$publication/package-manifest.json"
    $package = Get-DeliveryTarget $delivery "$publication/package"
    $manifestFile = Read-DeliveryFile $manifestPath -CaptureBytes
    if ($manifestFile.sha256 -cne $Members.packageManifestSha256) { throw 'Delivery package manifest hash mismatch.' }
    $manifest = ConvertFrom-DeliveryJson $manifestFile.contentBytes
    $null = @(Get-DeliveryTreeFiles $package)
    $trustedHelper = Get-DeliveryTarget $Repository 'scripts/PackageDistribution.Common.ps1'
    $helperFile = Read-DeliveryFile $trustedHelper
    if ((Read-DeliveryFile (Get-DeliveryTarget $delivery 'scripts/PackageDistribution.Common.ps1')).sha256 -cne $helperFile.sha256) {
        throw 'Use the repository helper version bundled with this delivery.'
    }
    . $trustedHelper
    Assert-RuntimeDistribution $package $manifest $Members.packageKind
    if ((ConvertTo-Json -InputObject @($manifest.basicPlugins) -Compress) -cne
        (ConvertTo-Json -InputObject @($Members.basicPlugins) -Compress)) { throw 'Delivery basicPlugins differs from the package.' }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($relative in @((Get-DeliverySupportPaths)) + @('DELIVERY.md','examples/runtime.standard.example.json','examples/runtime.developer.example.json')) {
        $null = $expected.Add("delivery/$relative")
    }
    $null = $expected.Add("delivery/$publication/package-manifest.json")
    foreach ($entry in @($manifest.files) + @($manifest.resources)) { $null = $expected.Add("delivery/$publication/package/$($entry.path)") }
    $rows = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $Members.members) {
        Assert-DeliveryRelative $row.path
        if (-not $expected.Contains($row.path) -or $row.sha256 -isnot [string] -or $row.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            $row.bytes -is [bool] -or $row.bytes -is [string] -or $row.bytes -isnot [ValueType] -or
            [decimal]$row.bytes -lt 0 -or [decimal]$row.bytes -ne [Math]::Floor([decimal]$row.bytes) -or -not $rows.TryAdd($row.path, $row)) {
            throw 'Invalid, duplicate or unlisted delivery member.'
        }
    }
    if ($rows.Count -ne $expected.Count) { throw 'Missing delivery members.' }
    $actual = @(Get-DeliveryTreeFiles $delivery)
    if ($actual.Count -ne $rows.Count) { throw 'Unexpected or missing outer delivery files.' }
    foreach ($file in $actual) {
        $relative = [IO.Path]::GetRelativePath($Output, $file).Replace('\', '/')
        if (-not $rows.ContainsKey($relative) -or $rows[$relative].path -cne $relative) { throw "Unlisted delivery file: $relative" }
        $identity = Read-DeliveryFile $file
        if ($identity.sha256 -cne $rows[$relative].sha256 -or $identity.bytes -ne $rows[$relative].bytes) { throw "Delivery hash mismatch: $relative" }
    }
    $lock = ConvertFrom-DeliveryJson (Read-DeliveryFile (Get-DeliveryTarget $delivery 'dependencies.lock.json') -CaptureBytes).contentBytes
    if ($lock.bepinex.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        (Read-DeliveryFile (Get-DeliveryTarget $delivery '.tools/bepinex-789.zip')).sha256 -cne $lock.bepinex.sha256) { throw 'Pinned dependency archive hash mismatch.' }
    $archivePath = Get-DeliveryTarget $Output "$DeliveryLabel.zip"
    $archiveFile = Read-DeliveryFile $archivePath
    $archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $zip = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $zip.Entries) {
                Assert-DeliveryRelative $entry.FullName
                $unixType = ($entry.ExternalAttributes -shr 16) -band 0xf000
                if (-not $entry.Name -or $unixType -eq 0xa000 -or
                    ($entry.ExternalAttributes -band [int][IO.FileAttributes]::ReparsePoint) -or
                    -not $seen.Add($entry.FullName) -or -not $rows.ContainsKey($entry.FullName) -or
                    $rows[$entry.FullName].path -cne $entry.FullName -or $entry.Length -ne $rows[$entry.FullName].bytes) {
                    throw "Unsafe, duplicate or unlisted ZIP member: $($entry.FullName)"
                }
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                if ($hash -cne $rows[$entry.FullName].sha256) { throw "ZIP member hash mismatch: $($entry.FullName)" }
            }
            if ($seen.Count -ne $rows.Count) { throw 'ZIP is missing delivery members.' }
        } finally { $zip.Dispose() }
        $archiveStream.Position = 0
        if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($archiveStream)).ToLowerInvariant() -cne $archiveFile.sha256) { throw 'ZIP changed during readback.' }
    } finally { $archiveStream.Dispose() }
    # ZIP readback can take time. Finish by sealing the expanded files as well.
    $finishedFiles = @(Get-DeliveryTreeFiles $delivery)
    if ($finishedFiles.Count -ne $rows.Count) { throw 'Expanded delivery changed during ZIP readback.' }
    foreach ($file in $finishedFiles) {
        $relative = [IO.Path]::GetRelativePath($Output, $file).Replace('\', '/')
        if (-not $rows.ContainsKey($relative) -or $rows[$relative].path -cne $relative) { throw 'Expanded delivery changed during ZIP readback.' }
        $identity = Read-DeliveryFile $file
        if ($identity.sha256 -cne $rows[$relative].sha256 -or $identity.bytes -ne $rows[$relative].bytes) {
            throw "Expanded delivery changed during ZIP readback: $relative"
        }
    }
    if ((Read-DeliveryFile $manifestPath).sha256 -cne $manifestFile.sha256 -or
        (Read-DeliveryFile $trustedHelper).sha256 -cne $helperFile.sha256) { throw 'Package or helper changed during readback.' }
    return [pscustomobject]@{packageKind=$Members.packageKind;packageManifestSha256=$manifestFile.sha256;memberCount=$rows.Count;archiveSha256=$archiveFile.sha256;archiveBytes=$archiveFile.bytes}
}

function Invoke-PreviewDeliveryReadback([string]$Output, [string]$Repository, [string]$DeliveryLabel) {
    $outputPath = Resolve-DeliveryPath $Output
    $repositoryPath = Resolve-DeliveryPath $Repository
    Assert-DeliveryLabel $DeliveryLabel
    $membersPath = Get-DeliveryTarget $outputPath "$DeliveryLabel.members.json"
    $receiptPath = Get-DeliveryTarget $outputPath "$DeliveryLabel.receipt.json"
    $memberFile = Read-DeliveryFile $membersPath -CaptureBytes
    $receiptFile = Read-DeliveryFile $receiptPath -CaptureBytes
    $members = ConvertFrom-DeliveryJson $memberFile.contentBytes
    $receipt = ConvertFrom-DeliveryJson $receiptFile.contentBytes
    if ($receipt.schemaVersion -ne 1 -or $receipt.kind -cne 'RizToolbox.PreviewDeliveryReceipt' -or
        $receipt.label -cne $DeliveryLabel -or $receipt.acceptance -cne 'preview/unverified' -or
        $receipt.readbackStatus -cne 'all-members-hash-matched' -or $receipt.sourceConsistency -cne 'matched-at-pack-finish' -or
        $receipt.membersManifestSha256 -cne $memberFile.sha256) { throw 'Invalid delivery receipt.' }
    foreach ($key in @('payloadProgramsExecuted','installExecuted','gameExecuted','realMachineAcceptanceVerified','productionAdmission')) {
        if ($receipt[$key] -isnot [bool] -or $receipt[$key]) { throw "Unexpected delivery acceptance claim: $key" }
    }
    $verified = Assert-DeliveryContent $outputPath $repositoryPath $DeliveryLabel $members
    if ($receipt.packageKind -cne $verified.packageKind -or $receipt.packageManifestSha256 -cne $verified.packageManifestSha256 -or
        $receipt.archiveSha256 -cne $verified.archiveSha256 -or $receipt.archiveBytes -ne $verified.archiveBytes -or
        $receipt.memberCount -ne $verified.memberCount -or (Read-DeliveryFile $membersPath).sha256 -cne $memberFile.sha256 -or
        (Read-DeliveryFile $receiptPath).sha256 -cne $receiptFile.sha256) { throw 'Delivery receipt or sidecars changed/mismatched.' }
    return [pscustomobject]@{outputDirectory=$outputPath;label=$DeliveryLabel;packageKind=$verified.packageKind;memberCount=$verified.memberCount;
        archiveSha256=$verified.archiveSha256;status='all-members-hash-matched';acceptance='preview/unverified';productionAdmission=$false;realMachineAcceptanceVerified=$false}
}

if ($LibraryOnly) { return }
if (-not $OutputDirectory -or -not $RepositoryRoot -or -not $Label) { throw 'Specify OutputDirectory, RepositoryRoot and Label.' }
Invoke-PreviewDeliveryReadback $OutputDirectory $RepositoryRoot $Label
