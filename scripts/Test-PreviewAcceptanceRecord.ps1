#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$RecordPath,
    [string]$PackageRoot,
    [string]$ManifestPath,
    [string]$InstalledRoot,
    [string]$OutputDirectory,
    [string]$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')),
    [switch]$LibraryOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Shared by the collector. LibraryOnly defines functions without collecting or validating.
function Resolve-PreviewPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\x00-\x1f*?"<>|\[\]]' -or
        $Path.StartsWith('\\?\') -or $Path.StartsWith('\\.\')) { throw 'Use an ordinary, literal filesystem path.' }
    $parts = $Path.Replace('\', '/').Split('/', [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($part in $parts) {
        if ($part -in @('.', '..') -or $part -match '[. ]$' -or
            $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw "Unsafe path segment: $part" }
    }
    if (($Path -replace '^[A-Za-z]:', '') -match ':') { throw 'Providers, streams, and device paths are not supported.' }
    [Management.Automation.ProviderInfo]$provider = $null
    [Management.Automation.PSDriveInfo]$drive = $null
    $native = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path, [ref]$provider, [ref]$drive)
    if ($provider.Name -cne 'FileSystem') { throw 'Only the filesystem provider is supported.' }
    $full = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($native))
    Assert-PreviewPathWithoutLinks $full
    return $full
}

function Get-PreviewItemOrNull([string]$Path) {
    try { return Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch {
        if ($_.CategoryInfo.Category -eq [Management.Automation.ErrorCategory]::ObjectNotFound) { return $null }
        throw
    }
}

function Assert-PreviewPathWithoutLinks([string]$Path) {
    for ($cursor = [IO.Path]::GetFullPath($Path); $cursor; $cursor = Split-Path -Parent $cursor) {
        $item = Get-PreviewItemOrNull $cursor
        if ($null -eq $item) { continue }
        $linkType = $item.PSObject.Properties['LinkType']
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            ($null -ne $linkType -and -not [string]::IsNullOrEmpty([string]$linkType.Value))) {
            throw "Linked paths are not supported: $cursor"
        }
    }
}

function Assert-PreviewDirectory([string]$Path) {
    Assert-PreviewPathWithoutLinks $Path
    $item = Get-PreviewItemOrNull $Path
    if ($null -eq $item -or -not $item.PSIsContainer) { throw "Missing directory: $Path" }
}

function Test-PreviewPathWithin([string]$Root, [string]$Path) {
    $prefix = if ([IO.Path]::EndsInDirectorySeparator($Root)) { $Root } else { $Root + [IO.Path]::DirectorySeparatorChar }
    return $Root.Equals($Path, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-PreviewOutputIsolation([string]$Output, [string[]]$DirectorySources, [string[]]$FileSources) {
    Assert-PreviewPathWithoutLinks $Output
    foreach ($source in $DirectorySources) {
        if (-not $source) { continue }
        if ((Test-PreviewPathWithin $source $Output) -or (Test-PreviewPathWithin $Output $source)) {
            throw "Evidence output overlaps a read directory: $source"
        }
    }
    foreach ($source in $FileSources) {
        if (Test-PreviewPathWithin $Output $source) { throw "Evidence output contains a source file: $source" }
    }
}

function New-PreviewEvidenceDirectory([string]$Output) {
    Assert-PreviewDirectory (Split-Path -Parent $Output)
    Assert-PreviewPathWithoutLinks $Output
    if ($null -ne (Get-PreviewItemOrNull $Output)) { throw 'Use a fresh evidence output directory.' }
    # No Force: creation must fail for an existing directory. Every file uses CreateNew as well.
    $null = New-Item -ItemType Directory -Path $Output -ErrorAction Stop
    Assert-PreviewDirectory $Output
}

function Write-PreviewNewBytes([string]$Path, [byte[]]$Bytes) {
    Assert-PreviewPathWithoutLinks $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    Assert-PreviewPathWithoutLinks $Path
}

function Write-PreviewNewJson([string]$Path, $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 32
    Write-PreviewNewBytes $Path ([Text.UTF8Encoding]::new($false).GetBytes($json + "`n"))
}

function Read-PreviewFile([string]$Path, [switch]$CaptureBytes) {
    $full = Resolve-PreviewPath $Path
    $item = Get-PreviewItemOrNull $full
    if ($null -eq $item -or $item.PSIsContainer) { throw "Missing source file: $full" }
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        Assert-PreviewPathWithoutLinks $full
        $item.Refresh()
        $length = $stream.Length
        $created = $item.CreationTimeUtc.ToString('o')
        $written = $item.LastWriteTimeUtc.ToString('o')
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $hash = [Convert]::ToHexString($algorithm.ComputeHash($stream)).ToLowerInvariant() }
        finally { $algorithm.Dispose() }
        $bytes = $null
        if ($CaptureBytes) {
            if ($length -gt 16MB) { throw 'JSON input exceeds the 16 MiB limit.' }
            $stream.Position = 0
            $buffer = [IO.MemoryStream]::new()
            try { $stream.CopyTo($buffer); $bytes = $buffer.ToArray() }
            finally { $buffer.Dispose() }
        }
        $item.Refresh()
        if ($stream.Length -ne $length -or $item.Length -ne $length -or
            $item.CreationTimeUtc.ToString('o') -cne $created -or $item.LastWriteTimeUtc.ToString('o') -cne $written) {
            throw "Source identity changed while reading: $full"
        }
        Assert-PreviewPathWithoutLinks $full
        return [ordered]@{
            identity = [ordered]@{path=$full;sha256=$hash;bytes=$length;createdUtc=$created;lastWriteUtc=$written}
            contentBytes = $bytes
        }
    } finally { $stream.Dispose() }
}

function Assert-PreviewJsonKeys($Element, [string]$Location = '$') {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw "Duplicate JSON key at $Location.$($property.Name)" }
            Assert-PreviewJsonKeys $property.Value "$Location.$($property.Name)"
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($child in $Element.EnumerateArray()) { Assert-PreviewJsonKeys $child $Location }
    }
}

function ConvertFrom-PreviewJsonElement($Element) {
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $value = [ordered]@{}
        foreach ($property in $Element.EnumerateObject()) { $value.Add($property.Name, (ConvertFrom-PreviewJsonElement $property.Value)) }
        return $value
    }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $items = [Collections.Generic.List[object]]::new()
        foreach ($child in $Element.EnumerateArray()) { $items.Add((ConvertFrom-PreviewJsonElement $child)) }
        return ,($items.ToArray())
    }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::String) { return $Element.GetString() }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Number) {
        [long]$integer = 0
        if ($Element.TryGetInt64([ref]$integer)) { return $integer }
        return $Element.GetDouble()
    }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::True) { return $true }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::False) { return $false }
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Null) { return $null }
    throw 'Unsupported JSON value.'
}

function ConvertFrom-PreviewJson([byte[]]$Bytes) {
    $json = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    if ($json.Length -gt 0 -and $json[0] -eq [char]0xfeff) { $json = $json.Substring(1) }
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        Assert-PreviewJsonKeys $document.RootElement
        # Keep ISO timestamps as strings on every supported PowerShell version.
        $value = ConvertFrom-PreviewJsonElement $document.RootElement
    }
    finally { $document.Dispose() }
    if ($value -isnot [Collections.IDictionary]) { throw 'JSON must contain an object.' }
    return $value
}

function Assert-PreviewFields($Value, [string[]]$Allowed, [string[]]$Required, [string]$Location) {
    if ($Value -isnot [Collections.IDictionary]) { throw "Expected an object: $Location" }
    foreach ($key in $Value.Keys) { if ($key -cnotin $Allowed) { throw "Unknown field: $Location.$key" } }
    foreach ($key in $Required) { if (-not $Value.Contains($key)) { throw "Missing field: $Location.$key" } }
}

function Get-PreviewValue($Object, [string]$Key, $Default = $null) {
    if ($Object.Contains($Key)) { return ,($Object[$Key]) }
    return ,$Default
}

function Get-PreviewText($Value, [string]$Location, [switch]$Required) {
    if ($null -eq $Value -and -not $Required) { return $null }
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 8192) {
        throw "Expected nonempty text: $Location"
    }
    return $Value
}

function Get-PreviewUtc($Value, [string]$Location, [switch]$Required) {
    if ($null -eq $Value -and -not $Required) { return $null }
    $text = Get-PreviewText $Value $Location -Required
    [DateTimeOffset]$time = [DateTimeOffset]::MinValue
    if ($text -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$' -or
        -not [DateTimeOffset]::TryParse($text, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref]$time) -or $time.Offset -ne [TimeSpan]::Zero) {
        throw "Expected an ISO 8601 UTC timestamp: $Location"
    }
    return $time.ToUniversalTime().ToString('o')
}

function Get-PreviewHash($Value, [string]$Location) {
    if ($null -eq $Value) { return $null }
    if ($Value -isnot [string] -or $Value -cnotmatch '^[0-9a-f]{64}$') { throw "Expected lowercase SHA256: $Location" }
    return $Value
}

function Get-PreviewStrings($Value, [string]$Location) {
    if ($null -eq $Value) { return @() }
    if ($Value -isnot [array]) { throw "Expected an array: $Location" }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Value) {
        $text = Get-PreviewText $entry $Location -Required
        if (-not $seen.Add($text)) { throw "Duplicate entry: $Location" }
        $text
    }
}

function Get-PreviewNumber($Value, [string]$Location, [switch]$Integer) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [bool] -or $Value -is [string] -or $Value -isnot [ValueType] -or
        -not [double]::IsFinite([double]$Value) -or [double]$Value -lt 0 -or
        ($Integer -and [Math]::Floor([double]$Value) -ne [double]$Value)) { throw "Expected a nonnegative number: $Location" }
    return $Value
}

function Get-PreviewOutcome($Value, [string]$Location, [switch]$Experimental) {
    if ($null -eq $Value) { $Value = [ordered]@{} }
    Assert-PreviewFields $Value @('status','detail','evidenceIds') @() $Location
    $status = Get-PreviewValue $Value 'status' 'untested'
    $allowed = if ($Experimental) { @('untested','passed','failed','unsupported') } else { @('untested','passed','failed') }
    if ($status -isnot [string] -or $status -cnotin $allowed) { throw "Invalid result status: $Location" }
    return [ordered]@{
        status=$status
        detail=(Get-PreviewText (Get-PreviewValue $Value 'detail') "$Location.detail")
        evidenceIds=@(Get-PreviewStrings (Get-PreviewValue $Value 'evidenceIds') "$Location.evidenceIds")
    }
}

function Get-PreviewCostRun($Value, [string]$Location) {
    if ($null -eq $Value) { return $null }
    $textFields = @('sessionId','machineId','scene','runtimeMode','measurementMethod','recoveryDetail')
    $numberFields = @('warmedUpSeconds','durationSeconds','pluginCount','managedP95Ms','managedP99Ms',
        'steadyAttributableMaxMs','frameIntervalP95Ms','frameIntervalP99Ms','transitionMaxMs','scanMaxMs','uploadMaxMs','controlMaxMs')
    Assert-PreviewFields $Value ($textFields + $numberFields + @('startedUtc','endedUtc','includes','excludes','evidenceIds')) @() $Location
    $run = [ordered]@{}
    foreach ($key in $textFields) { $run[$key] = Get-PreviewText (Get-PreviewValue $Value $key) "$Location.$key" }
    if ($null -ne $run.runtimeMode -and $run.runtimeMode -cnotin @('Standard','Developer','Original')) { throw "Invalid runtime mode: $Location" }
    foreach ($key in $numberFields) {
        $run[$key] = Get-PreviewNumber (Get-PreviewValue $Value $key) "$Location.$key" -Integer:($key -eq 'pluginCount')
    }
    foreach ($key in @('startedUtc','endedUtc')) { $run[$key] = Get-PreviewUtc (Get-PreviewValue $Value $key) "$Location.$key" }
    foreach ($key in @('includes','excludes','evidenceIds')) { $run[$key] = @(Get-PreviewStrings (Get-PreviewValue $Value $key) "$Location.$key") }
    return $run
}

function Get-PreviewDeclaration($Value) {
    Assert-PreviewFields $Value @('schemaVersion','session','scenarios','checks','experimental','cost','evidence','ownedWavStatement') @('schemaVersion','session') 'declaration'
    if ($Value.schemaVersion -is [bool] -or $Value.schemaVersion -is [string] -or $Value.schemaVersion -ne 1) { throw 'Unsupported declaration schema.' }
    $session = $Value.session
    Assert-PreviewFields $session @('id','kind','machineId','runtimeMode','scene','gameVersion','operator','startedUtc','endedUtc','manifestSha256') @('id') 'session'
    $normalizedSession = [ordered]@{id=(Get-PreviewText $session.id 'session.id' -Required)}
    $normalizedSession.kind = Get-PreviewValue $session 'kind' 'unverified'
    if ($normalizedSession.kind -isnot [string] -or $normalizedSession.kind -cnotin @('unverified','current-real-machine','historical')) { throw 'Invalid session kind.' }
    foreach ($key in @('machineId','scene','gameVersion','operator')) { $normalizedSession[$key] = Get-PreviewText (Get-PreviewValue $session $key) "session.$key" }
    $normalizedSession.runtimeMode = Get-PreviewValue $session 'runtimeMode' 'unrecorded'
    if ($normalizedSession.runtimeMode -isnot [string] -or $normalizedSession.runtimeMode -cnotin @('Standard','Developer','unrecorded')) { throw 'Invalid session runtimeMode.' }
    foreach ($key in @('startedUtc','endedUtc')) { $normalizedSession[$key] = Get-PreviewUtc (Get-PreviewValue $session $key) "session.$key" }
    $normalizedSession.manifestSha256 = Get-PreviewHash (Get-PreviewValue $session 'manifestSha256') 'session.manifestSha256'
    $declaration = [ordered]@{schemaVersion=1;session=$normalizedSession}
    foreach ($group in @('scenarios','checks','experimental')) {
        $keys = switch ($group) {
            'scenarios' { @('song-score-panel','color-theme-restore','shortcut-panel-owned-wav') }
            'checks' { @('installation','disable-restoration','restart-config','plugin-coexistence','fault-recovery','cost') }
            'experimental' { @('rate','seek') }
        }
        $values = Get-PreviewValue $Value $group ([ordered]@{})
        Assert-PreviewFields $values $keys @() $group
        $declaration[$group] = [ordered]@{}
        foreach ($key in $keys) { $declaration[$group][$key] = Get-PreviewOutcome (Get-PreviewValue $values $key) "$group.$key" -Experimental:($group -eq 'experimental') }
    }
    $cost = Get-PreviewValue $Value 'cost' ([ordered]@{})
    $costKeys = @('standardEmptyMods','basicScenarios','originalBaseline','developer')
    Assert-PreviewFields $cost $costKeys @() 'cost'
    $declaration.cost = [ordered]@{}
    foreach ($key in $costKeys) { $declaration.cost[$key] = Get-PreviewCostRun (Get-PreviewValue $cost $key) "cost.$key" }
    $declaration.ownedWavStatement = Get-PreviewText (Get-PreviewValue $Value 'ownedWavStatement') 'ownedWavStatement'
    $evidence = Get-PreviewValue $Value 'evidence' @()
    if ($null -eq $evidence -or $evidence -isnot [array]) { throw 'evidence must be an array.' }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($entry in $evidence) {
        Assert-PreviewFields $entry @('id','path','role','capturedUtc','sessionId','manifestSha256','machineId','scene','runtimeMode','description','declaredSha256') @('id','path','role') 'evidence'
        $id = Get-PreviewText $entry.id 'evidence.id' -Required
        $source = Get-PreviewText $entry.path 'evidence.path' -Required
        if (-not [IO.Path]::IsPathFullyQualified($source)) { throw 'Declaration evidence paths must be absolute.' }
        $path = Resolve-PreviewPath $source
        if (-not $ids.Add($id) -or -not $paths.Add($path)) { throw 'Duplicate evidence ID or path.' }
        if ($entry.role -isnot [string] -or $entry.role -cnotin @('current-candidate','original-baseline','historical-context')) { throw 'Invalid evidence role.' }
        $row = [ordered]@{id=$id;path=$path;role=$entry.role}
        foreach ($key in @('sessionId','machineId','scene','runtimeMode','description')) { $row[$key] = Get-PreviewText (Get-PreviewValue $entry $key) "evidence.$key" }
        $row.capturedUtc = Get-PreviewUtc (Get-PreviewValue $entry 'capturedUtc') 'evidence.capturedUtc'
        foreach ($key in @('manifestSha256','declaredSha256')) { $row[$key] = Get-PreviewHash (Get-PreviewValue $entry $key) "evidence.$key" }
        $rows.Add($row)
    }
    $declaration.evidence = @($rows.ToArray())
    $knownIds = @($declaration.evidence | ForEach-Object { $_.id })
    foreach ($group in @('scenarios','checks','experimental')) {
        foreach ($outcome in $declaration[$group].Values) {
            foreach ($id in $outcome.evidenceIds) { if ($id -cnotin $knownIds) { throw "Unknown evidence ID: $id" } }
        }
    }
    foreach ($run in $declaration.cost.Values) {
        if ($null -eq $run) { continue }
        foreach ($id in $run.evidenceIds) { if ($id -cnotin $knownIds) { throw "Unknown cost evidence ID: $id" } }
    }
    return $declaration
}

function Test-PreviewEvidenceBinding($Declaration, [string[]]$Ids, $Context, [string]$Role, [string]$ManifestSha256) {
    if ($Ids.Count -eq 0 -or -not $Context.id -or $null -eq $Context.startedUtc -or $null -eq $Context.endedUtc -or
        -not $Context.machineId -or -not $Context.scene -or -not $Context.runtimeMode) { return $false }
    $start = [DateTimeOffset]::Parse($Context.startedUtc, [Globalization.CultureInfo]::InvariantCulture)
    $end = [DateTimeOffset]::Parse($Context.endedUtc, [Globalization.CultureInfo]::InvariantCulture)
    if ($end -lt $start -or $end -gt [DateTimeOffset]::UtcNow) { return $false }
    foreach ($id in $Ids) {
        $matches = @($Declaration.evidence | Where-Object { $_.id -ceq $id })
        if ($matches.Count -ne 1) { return $false }
        $entry = $matches[0]
        if ($entry.role -cne $Role -or $entry.sessionId -cne $Context.id -or $entry.machineId -cne $Context.machineId -or
            $entry.scene -cne $Context.scene -or $entry.runtimeMode -cne $Context.runtimeMode -or $null -eq $entry.capturedUtc -or
            ($Role -ceq 'current-candidate' -and $entry.manifestSha256 -cne $ManifestSha256)) { return $false }
        $captured = [DateTimeOffset]::Parse($entry.capturedUtc, [Globalization.CultureInfo]::InvariantCulture)
        if ($captured -lt $start -or $captured -gt $end) { return $false }
    }
    return $true
}

function Get-PreviewCostAssessment($Declaration, [string]$ManifestSha256) {
    $missing = [Collections.Generic.List[string]]::new()
    $failed = [Collections.Generic.List[string]]::new()
    foreach ($name in @('standardEmptyMods','basicScenarios','originalBaseline','developer')) {
        $run = $Declaration.cost[$name]
        if ($null -eq $run) {
            if ($name -ne 'developer') { $missing.Add("cost.$name") }
            continue
        }
        $context = [ordered]@{id=$run.sessionId;machineId=$run.machineId;scene=$run.scene;runtimeMode=$run.runtimeMode;startedUtc=$run.startedUtc;endedUtc=$run.endedUtc}
        $role = if ($name -eq 'originalBaseline') { 'original-baseline' } else { 'current-candidate' }
        if (-not (Test-PreviewEvidenceBinding $Declaration $run.evidenceIds $context $role $ManifestSha256) -or -not $run.measurementMethod) { $missing.Add("cost.$name.binding-or-method") }
        $mode = switch ($name) { 'originalBaseline' { 'Original' }; 'developer' { 'Developer' }; default { 'Standard' } }
        if ($run.runtimeMode -cne $mode) { $missing.Add("cost.$name.mode") }
        foreach ($metric in @('frameIntervalP95Ms','frameIntervalP99Ms','durationSeconds')) {
            if ($null -eq $run[$metric]) { $missing.Add("cost.$name.$metric") }
        }
        if ($name -ne 'originalBaseline') {
            foreach ($metric in @('managedP95Ms','managedP99Ms','steadyAttributableMaxMs','transitionMaxMs','scanMaxMs','uploadMaxMs','controlMaxMs')) {
                if ($null -eq $run[$metric]) { $missing.Add("cost.$name.$metric") }
            }
            if ('entry' -cnotin $run.includes -or 'host' -cnotin $run.includes -or
                'game-original' -cnotin $run.excludes -or 'game-original' -cin $run.includes -or
                -not $run.recoveryDetail) { $missing.Add("cost.$name.attribution-or-recovery") }
        }
        if ($name -eq 'standardEmptyMods') {
            if ($null -eq $run.pluginCount -or $run.pluginCount -ne 0 -or $null -eq $run.warmedUpSeconds -or
                $run.warmedUpSeconds -le 0 -or $null -eq $run.durationSeconds -or $run.durationSeconds -lt 60) { $missing.Add('cost.standardEmptyMods.warmed-standard-60s-empty-mods') }
            if ($null -ne $run.managedP95Ms -and $run.managedP95Ms -gt 1) { $failed.Add('cost.standardEmptyMods.p95>1ms') }
            if ($null -ne $run.managedP99Ms -and $run.managedP99Ms -gt 2) { $failed.Add('cost.standardEmptyMods.p99>2ms') }
        }
        if ($name -eq 'originalBaseline' -and ($null -eq $run.pluginCount -or $run.pluginCount -ne 0 -or
            $null -eq $run.warmedUpSeconds -or $run.warmedUpSeconds -le 0 -or $null -eq $run.durationSeconds -or $run.durationSeconds -lt 60)) {
            $missing.Add('cost.originalBaseline.equivalent-warmed-60s-empty-mods')
        }
        if ($name -eq 'basicScenarios' -and $null -ne $run.steadyAttributableMaxMs -and $run.steadyAttributableMaxMs -ge 50) { $failed.Add('cost.basicScenarios.attributable-steady-stall>=50ms') }
        foreach ($pair in @(@('managedP95Ms','managedP99Ms'), @('frameIntervalP95Ms','frameIntervalP99Ms'))) {
            if ($null -ne $run[$pair[0]] -and $null -ne $run[$pair[1]] -and $run[$pair[0]] -gt $run[$pair[1]]) { $failed.Add("cost.$name.percentile-order") }
        }
    }
    $baseline = $Declaration.cost.originalBaseline
    $candidate = $Declaration.cost.standardEmptyMods
    if ($null -ne $baseline -and $null -ne $candidate -and
        ($baseline.machineId -cne $candidate.machineId -or $baseline.scene -cne $candidate.scene)) { $missing.Add('cost.same-machine-equivalent-scene-baseline') }
    return [ordered]@{
        uncoveredItems=@($missing.ToArray());failedItems=@($failed.ToArray())
        developerState=$(if ($null -eq $Declaration.cost.developer) { 'unmeasured' } else { 'reported-separately' })
        targets=[ordered]@{standardEmptyModsSeconds=60;managedP95Ms=1;managedP99Ms=2;basicSteadyStallRejectedAtMs=50}
    }
}

function Get-PreviewAssessment($Sources) {
    $declaration = $Sources.declaration.content
    $session = $declaration.session
    $hash = $Sources.candidate.manifest.sha256
    $failed = [Collections.Generic.List[string]]::new()
    $uncovered = [Collections.Generic.List[string]]::new()
    if ($session.kind -cne 'current-real-machine' -or $session.manifestSha256 -cne $hash -or $session.runtimeMode -cne 'Standard') {
        $uncovered.Add('current-candidate-Standard-real-machine-session')
    }
    foreach ($group in @('scenarios','checks')) {
        foreach ($key in $declaration[$group].Keys) {
            $outcome = $declaration[$group][$key]
            if ($outcome.status -ceq 'failed') { $failed.Add("$group.$key") }
            elseif ($outcome.status -ceq 'untested') { $uncovered.Add("$group.$key") }
            elseif (-not (Test-PreviewEvidenceBinding $declaration $outcome.evidenceIds $session 'current-candidate' $hash)) { $uncovered.Add("$group.$key.current-evidence-binding") }
        }
    }
    if ($Sources.installed.root -eq $null) { $uncovered.Add('installed-payload.not-requested') }
    else {
        foreach ($row in $Sources.installed.files) {
            if ($row.status -cne 'matched') { $failed.Add("installed-payload.$($row.relativePath).$($row.status)") }
            if ($row.ownership -cne 'matched') { $failed.Add("installed-ownership.$($row.relativePath).$($row.ownership)") }
        }
        $ownership = $Sources.installed.ownershipManifest
        if ($ownership.state -ceq 'missing') { $uncovered.Add('installation-ownership.manifest-missing') }
        elseif ($ownership.state -cne 'finalized') { $failed.Add("installation-ownership.$($ownership.state)") }
        foreach ($row in $ownership.files) {
            if ($row.requiredForCandidate -and $row.status -cne 'matched') { $failed.Add("owned-installation-file.$($row.relativePath).$($row.status)") }
        }
    }
    if ($Sources.selected.basicPluginFiles.Count -eq 0 -and $Sources.candidate.basicPlugins.Count -eq 0) { $uncovered.Add('selected-basic-plugin-files') }
    if ($Sources.selected.ownedWavFiles.Count -eq 0 -or -not $declaration.ownedWavStatement) { $uncovered.Add('selected-owned-WAV-and-ownership-declaration') }
    $cost = Get-PreviewCostAssessment $declaration $hash
    foreach ($item in $cost.failedItems) { $failed.Add($item) }
    foreach ($item in $cost.uncoveredItems) { $uncovered.Add($item) }
    $experiments = [ordered]@{}
    foreach ($key in @('rate','seek')) {
        $outcome = $declaration.experimental[$key]
        $state = $outcome.status
        if ($state -ceq 'passed') {
            $state = if (Test-PreviewEvidenceBinding $declaration $outcome.evidenceIds $session 'current-candidate' $hash) { 'reported-pass-with-bound-evidence' } else { 'reported-pass-without-current-binding' }
        }
        $experiments[$key] = [ordered]@{experimental=$true;state=$state}
    }
    $state = if ($failed.Count) { 'failed' } elseif ($uncovered.Count) { 'incomplete' } else { 'reported-complete-awaiting-human-review' }
    return [ordered]@{
        state=$state;failedItems=@($failed.ToArray());uncoveredItems=@($uncovered.ToArray());cost=$cost;experimental=$experiments
        provenance='user-declaration-and-read-only-file-snapshots';realMachineAcceptanceVerified=$false
        acceptance='preview/unverified';productionAdmission=$false
    }
}

function Get-PreviewRepositoryHelper([string]$Root) {
    $repository = Resolve-PreviewPath $Root
    Assert-PreviewDirectory $repository
    $helper = Resolve-PreviewPath (Join-Path $repository 'scripts/PackageDistribution.Common.ps1')
    $item = Get-PreviewItemOrNull $helper
    if ($null -eq $item -or $item.PSIsContainer) { throw 'Missing repository PackageDistribution.Common.ps1.' }
    return $helper
}

function Get-PreviewSelectedFiles([string[]]$Paths, [switch]$Wav) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        $full = Resolve-PreviewPath $path
        if (-not $seen.Add($full)) { throw 'Duplicate selected file path.' }
        if ($Wav -and -not [IO.Path]::GetExtension($full).Equals('.wav', [StringComparison]::OrdinalIgnoreCase)) { throw 'Owned WAV inputs must have a .wav extension.' }
        (Read-PreviewFile $full).identity
    }
}

function Get-PreviewInstallationOwnership([string]$Root, [string[]]$CandidatePaths) {
    $path = Resolve-PreviewPath (Join-Path $Root 'BepInEx/RizToolbox/installation.json')
    $index = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = [Collections.Generic.List[object]]::new()
    $report = [ordered]@{path=$path;identity=$null;state='missing';error=$null;files=@()}
    if ($null -eq (Get-PreviewItemOrNull $path)) { return [ordered]@{report=$report;index=$index} }
    try {
        $capture = Read-PreviewFile $path -CaptureBytes
        $report.identity = $capture.identity
        $manifest = ConvertFrom-PreviewJson $capture.contentBytes
        if ($manifest.owner -cne 'riz_toolbox' -or $manifest.gameRoot -isnot [string] -or
            -not $Root.Equals((Resolve-PreviewPath $manifest.gameRoot), [StringComparison]::OrdinalIgnoreCase) -or
            $manifest.files -isnot [array] -or $manifest.pending -isnot [array]) {
            throw 'Installation ownership identity or file map is invalid.'
        }
        $report.state = if ($manifest.pending.Count -ne 0 -or $manifest.Contains('transaction')) { 'pending-transaction' } else { 'finalized' }
        foreach ($entry in $manifest.files) {
            if ($entry.path -isnot [string] -or $entry.sha256 -isnot [string] -or
                $entry.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid owned file entry.' }
            $relative = (Assert-InstallPayloadPath $Root $entry.path).Replace('\','/')
            if (-not $index.TryAdd($relative, $entry.sha256)) { throw 'Duplicate owned file entry.' }
            $target = Resolve-PreviewPath (Join-Path $Root $relative)
            $item = Get-PreviewItemOrNull $target
            $actual = $null
            $status = 'missing'
            if ($null -ne $item) {
                if ($item.PSIsContainer) { $status = 'mismatched' }
                else {
                    $actual = (Read-PreviewFile $target).identity
                    $status = if ($actual.sha256 -ceq $entry.sha256) { 'matched' } else { 'mismatched' }
                }
            }
            $required = $relative -cin $CandidatePaths -or $relative -ceq 'BepInEx/core/dobby.dll' -or
                $relative -ceq 'doorstop_config.ini' -or $relative.StartsWith('dotnet/', [StringComparison]::Ordinal)
            $files.Add([ordered]@{relativePath=$relative;ownedSha256=$entry.sha256;requiredForCandidate=$required;status=$status;actual=$actual})
        }
    } catch {
        $report.state = 'invalid'
        $report.error = $_.Exception.Message
        $index.Clear()
    }
    $report.files = @($files.ToArray() | Sort-Object -Property relativePath)
    return [ordered]@{report=$report;index=$index}
}

function Get-PreviewCurrentSources($Request, [string]$ToolsDirectory, [string]$Output) {
    $repository = Resolve-PreviewPath $Request.repositoryRoot
    $package = Resolve-PreviewPath $Request.packageRoot
    $manifestPath = Resolve-PreviewPath $Request.manifestPath
    $declarationPath = Resolve-PreviewPath $Request.declarationPath
    $installed = if ($Request.installedRoot) { Resolve-PreviewPath $Request.installedRoot } else { $null }
    Assert-PreviewDirectory $package
    if ($installed) { Assert-PreviewDirectory $installed }
    $declarationCapture = Read-PreviewFile $declarationPath -CaptureBytes
    $declaration = Get-PreviewDeclaration (ConvertFrom-PreviewJson $declarationCapture.contentBytes)
    $toolPaths = @((Get-PreviewRepositoryHelper $repository),
        (Resolve-PreviewPath (Join-Path $repository 'scripts/Install.Common.ps1')),
        (Resolve-PreviewPath (Join-Path $ToolsDirectory 'New-PreviewAcceptanceRecord.ps1')),
        (Resolve-PreviewPath (Join-Path $ToolsDirectory 'Test-PreviewAcceptanceRecord.ps1')))
    $pluginPaths = @($Request.basicPluginFiles | ForEach-Object { Resolve-PreviewPath $_ })
    $wavPaths = @($Request.ownedWavFiles | ForEach-Object { Resolve-PreviewPath $_ })
    $filePaths = @($manifestPath,$declarationPath) + $toolPaths + $pluginPaths + $wavPaths + @($declaration.evidence | ForEach-Object { $_.path })
    Assert-PreviewOutputIsolation $Output @($package,$installed) $filePaths
    $manifestCapture = Read-PreviewFile $manifestPath -CaptureBytes
    $manifest = ConvertFrom-PreviewJson $manifestCapture.contentBytes
    Assert-RuntimeDistribution $package $manifest 'Runtime'
    $ownership = if ($installed) {
        . (Join-Path $repository 'scripts/Install.Common.ps1')
        Get-PreviewInstallationOwnership $installed @($manifest.files.path)
    } else { $null }
    $payload = [Collections.Generic.List[object]]::new()
    $installedFiles = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($manifest.files | Sort-Object -Property path)) {
        # Assert-RuntimeDistribution has validated this complete manifest-selected file set.
        $relative = $entry.path
        $identity = (Read-PreviewFile (Join-Path $package $relative)).identity
        if ($identity.sha256 -cne $entry.sha256) { throw "Runtime payload changed: $relative" }
        $payload.Add([ordered]@{relativePath=$relative;identity=$identity})
        if (-not $installed) { continue }
        $target = Resolve-PreviewPath (Join-Path $installed $relative)
        $item = Get-PreviewItemOrNull $target
        $actual = $null
        $actualType = 'missing'
        $status = 'missing'
        if ($null -ne $item) {
            if ($item.PSIsContainer) {
                $actualType = 'directory'
                $actual = [ordered]@{path=$target;createdUtc=$item.CreationTimeUtc.ToString('o');lastWriteUtc=$item.LastWriteTimeUtc.ToString('o')}
                $status = 'mismatched'
            } else {
                $actualType = 'file'
                $actual = (Read-PreviewFile $target).identity
                $status = if ($actual.sha256 -ceq $identity.sha256 -and $actual.bytes -eq $identity.bytes) { 'matched' } else { 'mismatched' }
            }
        }
        $ownershipStatus = if ($ownership.report.state -cne 'finalized') { $ownership.report.state }
            elseif (-not $ownership.index.ContainsKey($relative)) { 'unowned' }
            elseif ($ownership.index[$relative] -cne $identity.sha256) { 'mismatched' }
            else { 'matched' }
        $installedFiles.Add([ordered]@{relativePath=$relative;expectedSha256=$identity.sha256;status=$status;ownership=$ownershipStatus;actualType=$actualType;actual=$actual})
    }
    $evidenceFiles = [Collections.Generic.List[object]]::new()
    foreach ($entry in $declaration.evidence) {
        $snapshot = (Read-PreviewFile $entry.path).identity
        if ($null -ne $entry.declaredSha256 -and $entry.declaredSha256 -cne $snapshot.sha256) { throw "Declared evidence hash mismatch: $($entry.id)" }
        $evidenceFiles.Add([ordered]@{id=$entry.id;role=$entry.role;identity=$snapshot})
    }
    $sources = [ordered]@{
        repositoryRoot=$repository
        tools=@($toolPaths | ForEach-Object { (Read-PreviewFile $_).identity })
        candidate=[ordered]@{
            packageKind='Runtime';acceptance='preview/unverified';packageRoot=$package;manifest=$manifestCapture.identity
            basicPlugins=(Get-PreviewValue $manifest 'basicPlugins' @());payload=@($payload.ToArray())
        }
        installed=[ordered]@{
            root=$installed;scope='candidate-payload-and-recorded-ownership'
            ownershipManifest=$(if ($ownership) { $ownership.report } else { $null })
            files=@($installedFiles.ToArray())
        }
        selected=[ordered]@{basicPluginFiles=@(Get-PreviewSelectedFiles $pluginPaths);ownedWavFiles=@(Get-PreviewSelectedFiles $wavPaths -Wav)}
        declaration=[ordered]@{origin=$declarationCapture.identity;copyRelativePath='declaration.json';content=$declaration}
        evidenceFiles=@($evidenceFiles.ToArray())
    }
    $sources.assessment = Get-PreviewAssessment $sources
    return [ordered]@{sources=$sources;declarationBytes=$declarationCapture.contentBytes}
}

function Assert-PreviewEqual($Expected, $Actual, [string]$Location = '$') {
    if ($null -eq $Expected) { if ($null -ne $Actual) { throw "Unexpected value: $Location" }; return }
    if ($Expected -is [Collections.IDictionary]) {
        Assert-PreviewFields $Actual @($Expected.Keys) @($Expected.Keys) $Location
        foreach ($key in $Expected.Keys) { Assert-PreviewEqual $Expected[$key] $Actual[$key] "$Location.$key" }
    } elseif ($Expected -is [array]) {
        if ($Actual -isnot [array] -or $Expected.Count -ne $Actual.Count) { throw "Array mismatch: $Location" }
        for ($i = 0; $i -lt $Expected.Count; $i++) { Assert-PreviewEqual $Expected[$i] $Actual[$i] "$Location[$i]" }
    } elseif ($Expected -is [bool]) {
        if ($Actual -isnot [bool] -or $Expected -ne $Actual) { throw "Boolean mismatch: $Location" }
    } elseif ($Expected -is [string]) {
        if ($Actual -isnot [string] -or $Expected -cne $Actual) { throw "Text mismatch: $Location" }
    } elseif ($Actual -is [bool] -or $Actual -is [string] -or $Actual -isnot [ValueType] -or $Expected -ne $Actual) {
        throw "Value mismatch: $Location"
    }
}

function Assert-PreviewRecordHeader($Record) {
    $fields = @('schemaVersion','kind','recordId','startedUtc','snapshotUtc','collectionStatus','programsExecuted','acceptance','productionAdmission','realMachineAcceptanceVerified','sources')
    Assert-PreviewFields $Record $fields $fields 'record'
    if ($Record.schemaVersion -is [bool] -or $Record.schemaVersion -is [string] -or $Record.schemaVersion -ne 1 -or
        $Record.kind -cne 'RizToolbox.PreviewAcceptanceRecord' -or $Record.recordId -isnot [string] -or $Record.recordId -cnotmatch '^[0-9a-f]{32}$' -or
        $Record.collectionStatus -cne 'snapshot-awaiting-final-source-seal' -or $Record.acceptance -cne 'preview/unverified' -or
        $Record.programsExecuted -isnot [bool] -or $Record.programsExecuted -or
        $Record.productionAdmission -isnot [bool] -or $Record.productionAdmission -or
        $Record.realMachineAcceptanceVerified -isnot [bool] -or $Record.realMachineAcceptanceVerified) { throw 'Invalid record identity or acceptance policy.' }
    $start = Get-PreviewUtc $Record.startedUtc 'record.startedUtc' -Required
    $snapshot = Get-PreviewUtc $Record.snapshotUtc 'record.snapshotUtc' -Required
    if ([DateTimeOffset]::Parse($snapshot) -lt [DateTimeOffset]::Parse($start) -or [DateTimeOffset]::Parse($snapshot) -gt [DateTimeOffset]::UtcNow) { throw 'Invalid collection interval.' }
}

function Get-PreviewRequestFromRecord($Record, [string]$Repository, [string]$Package, [string]$Manifest, [string]$Installed) {
    Assert-PreviewRecordHeader $Record
    if ($Record.sources.repositoryRoot -cne (Resolve-PreviewPath $Repository) -or
        $Record.sources.candidate.packageRoot -cne (Resolve-PreviewPath $Package) -or
        $Record.sources.candidate.manifest.path -cne (Resolve-PreviewPath $Manifest)) { throw 'The record does not identify the explicitly selected current candidate.' }
    $installedPath = if ($Installed) { Resolve-PreviewPath $Installed } else { $null }
    if ($Record.sources.installed.root -cne $installedPath) { throw 'Supply the same optional InstalledRoot that the record identifies.' }
    return [ordered]@{
        repositoryRoot=$Repository;packageRoot=$Package;manifestPath=$Manifest;installedRoot=$installedPath
        declarationPath=$Record.sources.declaration.origin.path
        basicPluginFiles=@($Record.sources.selected.basicPluginFiles | ForEach-Object { $_.path })
        ownedWavFiles=@($Record.sources.selected.ownedWavFiles | ForEach-Object { $_.path })
    }
}

function Assert-PreviewBundle([string]$Directory, $Record, $RecordIdentity, [switch]$RequireSeal) {
    Assert-PreviewDirectory $Directory
    $allowed = @('collection-start.json','declaration.json','acceptance-record.json')
    if ($RequireSeal) { $allowed += 'collection-complete.json' }
    $files = @(Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop)
    if ($files.Count -ne $allowed.Count) { throw 'Evidence bundle is incomplete or contains unexpected files.' }
    foreach ($file in $files) {
        Assert-PreviewPathWithoutLinks $file.FullName
        if ($file.PSIsContainer -or $file.Name -cnotin $allowed) { throw 'Unexpected evidence bundle content.' }
    }
    $declaration = Read-PreviewFile (Join-Path $Directory 'declaration.json') -CaptureBytes
    if ($declaration.identity.sha256 -cne $Record.sources.declaration.origin.sha256 -or
        $declaration.identity.bytes -ne $Record.sources.declaration.origin.bytes) { throw 'The preserved declaration does not match its original bytes.' }
    Assert-PreviewEqual $Record.sources.declaration.content (Get-PreviewDeclaration (ConvertFrom-PreviewJson $declaration.contentBytes)) 'declaration'
    $start = Read-PreviewFile (Join-Path $Directory 'collection-start.json') -CaptureBytes
    $expectedStart = [ordered]@{schemaVersion=1;kind='RizToolbox.PreviewCollectionStart';recordId=$Record.recordId;startedUtc=$Record.startedUtc;acceptance='preview/unverified';productionAdmission=$false}
    Assert-PreviewEqual $expectedStart (ConvertFrom-PreviewJson $start.contentBytes) 'collection-start'
    $completionIdentity = $null
    if ($RequireSeal) {
        $sealFile = Read-PreviewFile (Join-Path $Directory 'collection-complete.json') -CaptureBytes
        $seal = ConvertFrom-PreviewJson $sealFile.contentBytes
        $fields = @('schemaVersion','kind','recordId','completedUtc','recordSha256','declarationSha256','startSha256','sourceConsistency','acceptance','productionAdmission','realMachineAcceptanceVerified')
        Assert-PreviewFields $seal $fields $fields 'seal'
        $completed = Get-PreviewUtc $seal.completedUtc 'seal.completedUtc' -Required
        if ([DateTimeOffset]::Parse($completed) -lt [DateTimeOffset]::Parse($Record.snapshotUtc) -or [DateTimeOffset]::Parse($completed) -gt [DateTimeOffset]::UtcNow) { throw 'Invalid seal completion interval.' }
        $expected = [ordered]@{
            schemaVersion=1;kind='RizToolbox.PreviewCollectionComplete';recordId=$Record.recordId;completedUtc=$completed
            recordSha256=$RecordIdentity.sha256;declarationSha256=$declaration.identity.sha256;startSha256=$start.identity.sha256
            sourceConsistency='consistent-at-collection-finish';acceptance='preview/unverified';productionAdmission=$false;realMachineAcceptanceVerified=$false
        }
        Assert-PreviewEqual $expected $seal 'seal'
        $completionIdentity = $sealFile.identity
    }
    return [ordered]@{record=$RecordIdentity;declaration=$declaration.identity;start=$start.identity;completion=$completionIdentity}
}

function Write-PreviewFailure([string]$Directory, [string]$Name, [string]$Operation, $ErrorRecord, $Partial) {
    try {
        Write-PreviewNewJson (Join-Path $Directory $Name) ([ordered]@{
            schemaVersion=1;kind='RizToolbox.PreviewEvidenceFailure';operation=$Operation;failedUtc=[DateTimeOffset]::UtcNow.ToString('o')
            status='failed';acceptance='preview/unverified';productionAdmission=$false;realMachineAcceptanceVerified=$false
            error=$ErrorRecord.Exception.Message;partial=$Partial
        })
    } catch { Write-Warning "Failure details could not be written. Existing evidence remains at $Directory. $($_.Exception.Message)" }
}

if (-not $LibraryOnly) {
    foreach ($required in @('RecordPath','PackageRoot','ManifestPath','OutputDirectory','RepositoryRoot')) {
        if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $required -ValueOnly))) { throw "Supply -$required." }
    }
    . (Get-PreviewRepositoryHelper $RepositoryRoot)
    $recordFull = Resolve-PreviewPath $RecordPath
    if ([IO.Path]::GetFileName($recordFull) -cne 'acceptance-record.json') { throw 'Select the acceptance-record.json inside its original evidence bundle.' }
    $bundle = Split-Path -Parent $recordFull
    $output = Resolve-PreviewPath $OutputDirectory
    $recordCapture = Read-PreviewFile $recordFull -CaptureBytes
    $record = ConvertFrom-PreviewJson $recordCapture.contentBytes
    $request = Get-PreviewRequestFromRecord $record $RepositoryRoot $PackageRoot $ManifestPath $InstalledRoot
    $knownSources = @($recordFull, $request.declarationPath, $request.manifestPath) + $request.basicPluginFiles + $request.ownedWavFiles +
        @($record.sources.declaration.content.evidence | ForEach-Object { Resolve-PreviewPath $_.path })
    Assert-PreviewOutputIsolation $output @($bundle,(Resolve-PreviewPath $PackageRoot),$request.installedRoot) $knownSources
    New-PreviewEvidenceDirectory $output
    $partial = $null
    try {
        $startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Write-PreviewNewJson (Join-Path $output 'validation-start.json') ([ordered]@{
            schemaVersion=1;kind='RizToolbox.PreviewValidationStart';startedUtc=$startedUtc;recordPath=$recordFull
            acceptance='preview/unverified';productionAdmission=$false
        })
        $firstBundle = Assert-PreviewBundle $bundle $record $recordCapture.identity -RequireSeal
        $current = Get-PreviewCurrentSources $request $PSScriptRoot $output
        $partial = $current.sources
        Assert-PreviewEqual $record.sources $current.sources 'sources'
        # Re-read all inputs and every runtime payload at finish, including installed status.
        $finish = Get-PreviewCurrentSources $request $PSScriptRoot $output
        Assert-PreviewEqual $current.sources $finish.sources 'finish.sources'
        $recordFinish = Read-PreviewFile $recordFull -CaptureBytes
        Assert-PreviewEqual $recordCapture.identity $recordFinish.identity 'record.identity'
        Assert-PreviewEqual $record (ConvertFrom-PreviewJson $recordFinish.contentBytes) 'record'
        $finishBundle = Assert-PreviewBundle $bundle $record $recordFinish.identity -RequireSeal
        Assert-PreviewEqual $firstBundle $finishBundle 'bundle.identity'
        Write-PreviewNewJson (Join-Path $output 'validation-result.json') ([ordered]@{
            schemaVersion=1;kind='RizToolbox.PreviewRecordValidation';recordId=$record.recordId;recordSha256=$recordFinish.identity.sha256
            startedUtc=$startedUtc;completedUtc=[DateTimeOffset]::UtcNow.ToString('o');sourceConsistency='consistent-at-validation-finish'
            reportedAssessment=$finish.sources.assessment;programsExecuted=$false;realMachineAcceptanceVerified=$false
            acceptance='preview/unverified';productionAdmission=$false
        })
    } catch {
        Write-PreviewFailure $output 'validation-failure.json' 'validate-record' $_ $partial
        throw
    }
    Write-Output "Record and current source bytes are consistent. Acceptance remains preview/unverified. Validation evidence: $output"
}
