#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DiagnosticsRoot,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,80}$')][string]$SessionId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(4096, 67108864)][long]$MaxTotalBytes = 16777216,
    [ValidateRange(1, 64)][int]$MaxEventFiles = 16
)
$ErrorActionPreference = 'Stop'

function Assert-NoDiagnosticLink([string]$Path) {
    $candidate = [IO.Path]::GetFullPath($Path)
    while ($candidate) {
        if ([IO.File]::Exists($candidate) -or [IO.Directory]::Exists($candidate)) {
            if (([IO.File]::GetAttributes($candidate) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Diagnostic export cannot traverse links or reparse points.'
            }
        }
        $candidate = [IO.Path]::GetDirectoryName($candidate)
    }
}

function Read-BoundedDiagnosticFile([string]$Path, [long]$Remaining) {
    Assert-NoDiagnosticLink $Path
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $length = $stream.Length
        if ($length -gt $Remaining -or $length -gt 67108864) { throw 'Selected session exceeds the export byte budget.' }
        $bytes = [byte[]]::new([int]$length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) { throw 'Diagnostic file changed while being exported; retry the export.' }
            $offset += $read
        }
        return ,$bytes
    } finally { $stream.Dispose() }
}

$root = [IO.Path]::GetFullPath($DiagnosticsRoot)
$selected = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $SessionId))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Directory]::Exists($root) -or -not [IO.Directory]::Exists($selected)) { throw 'Selected diagnostic session does not exist.' }
Assert-NoDiagnosticLink $selected
Assert-NoDiagnosticLink ([IO.Path]::GetDirectoryName($destination))
if ([IO.File]::Exists($destination) -or [IO.Directory]::Exists($destination)) { throw 'OutputDirectory must be a new directory.' }
if ($destination.StartsWith($root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $destination.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputDirectory must be outside DiagnosticsRoot.' }

$metadataPath = [IO.Path]::Combine($selected, 'session.json')
$metadataBytes = Read-BoundedDiagnosticFile $metadataPath ([Math]::Min($MaxTotalBytes, 4096))
$metadata = [Text.Encoding]::UTF8.GetString($metadataBytes) | ConvertFrom-Json -AsHashtable
if ($metadata.schemaVersion -ne 1 -or $metadata.sessionId -cne $SessionId) { throw 'Session ownership metadata does not match the selected session.' }
[DateTimeOffset]$startedAt = $metadata.startedAt
$entries = @([IO.Directory]::EnumerateFileSystemEntries($selected) | Select-Object -First 129)
if ($entries.Count -gt 128) { throw 'Selected session exceeds the bounded directory scan.' }
$eventPaths = @($entries | Where-Object { [IO.Path]::GetFileName($_) -cmatch '^events-[0-9]{6,12}\.jsonl$' } | Sort-Object)
if ($eventPaths.Count -gt $MaxEventFiles) { throw 'Selected session exceeds MaxEventFiles; choose an explicit larger limit.' }

# Stage all bounded, parsed content in memory before creating the destination. Never copy neighboring files.
$budgetUsed = [long]$metadataBytes.Length
$staged = [Collections.Generic.List[object]]::new()
$exportedEvents = 0
$incompleteTails = 0
$utf8 = [Text.UTF8Encoding]::new($false, $true)
foreach ($path in $eventPaths) {
    if ([IO.Directory]::Exists($path)) { throw 'An event file is a directory.' }
    $bytes = Read-BoundedDiagnosticFile $path ($MaxTotalBytes - $budgetUsed)
    $budgetUsed += $bytes.Length
    $lastNewline = [Array]::LastIndexOf($bytes, [byte]10)
    if ($bytes.Length -gt 0 -and $lastNewline -ne $bytes.Length - 1) { $incompleteTails++ }
    $text = if ($lastNewline -ge 0) { $utf8.GetString($bytes, 0, $lastNewline + 1) } else { '' }
    $builder = [Text.StringBuilder]::new()
    foreach ($line in $text.Split("`n", [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($line.Length -gt 262144) { throw 'A diagnostic record exceeds the supported record limit.' }
        $record = $line | ConvertFrom-Json -AsHashtable
        if ($record.schemaVersion -ne 1 -or $record.sessionId -cne $SessionId) { throw 'An event belongs to a different or unsupported session.' }
        # Public schema only. No arbitrary properties from an altered source file are exported.
        $safe = [ordered]@{}
        foreach ($key in @('schemaVersion','sessionId','sequence','timestamp','level','eventCode','message','context','exception','fields')) {
            if ($record.ContainsKey($key)) { $safe[$key] = $record[$key] }
        }
        if ($safe.context) {
            $context = [ordered]@{}
            foreach ($key in @('pluginId','capabilityId','targetId','runId','sceneGeneration','correlationId')) {
                if ($safe.context.ContainsKey($key)) { $context[$key] = $safe.context[$key] }
            }
            $safe.context = $context
        }
        [void]$builder.Append(($safe | ConvertTo-Json -Depth 8 -Compress)).Append("`n")
        $exportedEvents++
    }
    $staged.Add(@{ Name = [IO.Path]::GetFileName($path); Bytes = $utf8.GetBytes($builder.ToString()) })
}

$snapshotPath = [IO.Path]::Combine($selected, 'snapshot.json')
if ([IO.File]::Exists($snapshotPath)) {
    $snapshotBytes = Read-BoundedDiagnosticFile $snapshotPath ([Math]::Min(65536, $MaxTotalBytes - $budgetUsed))
    $budgetUsed += $snapshotBytes.Length
    $snapshot = $utf8.GetString($snapshotBytes) | ConvertFrom-Json -AsHashtable
    if ($snapshot.sessionId -cne $SessionId) { throw 'Snapshot belongs to a different session.' }
    $safeSnapshot = [ordered]@{}
    foreach ($key in @('sessionId','startedAt','snapshotAt','minimumLevel','attempted','written','textWritten','filtered',
        'repeatSuppressed','rateSuppressed','diskFailures','diskSkipped','internalFailures','oversized','retainedFiles',
        'retainedBytes','trackedRepeatKeys','lastError','levels','enqueued','processed','queueDropped','queueAbandoned',
        'pendingEvents','pendingBytes','peakPendingEvents','peakPendingBytes','lastEnqueuedSequence',
        'lastProcessedSequence','lastSuccessfulFlushSequence','flushTimeouts','pendingFlushAttempts','peakPendingFlushAttempts',
        'acceptingWrites','consumerCompleted')) {
        if ($snapshot.ContainsKey($key)) { $safeSnapshot[$key] = $snapshot[$key] }
    }
    $staged.Add(@{ Name = 'snapshot.json'; Bytes = $utf8.GetBytes(($safeSnapshot | ConvertTo-Json -Depth 6)) })
}
$safeMetadata = [ordered]@{ schemaVersion = 1; sessionId = $SessionId; startedAt = $startedAt }
$staged.Add(@{ Name = 'session.json'; Bytes = $utf8.GetBytes(($safeMetadata | ConvertTo-Json)) })
$manifest = [ordered]@{
    schemaVersion = 1; sessionId = $SessionId; exportedAt = [DateTimeOffset]::UtcNow;
    exportedEvents = $exportedEvents; incompleteTailsDropped = $incompleteTails;
    snapshotIsOptional = $true; sourceMayBeActive = $true;
    exclusions = @('legacy toolbox.log', 'other sessions', 'status.json', 'settings', 'saves', 'account files', 'exports');
    files = @($staged | ForEach-Object {
        [ordered]@{ path = $_.Name; bytes = $_.Bytes.Length;
            sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($_.Bytes)).ToLowerInvariant() }
    })
}
$manifestBytes = $utf8.GetBytes(($manifest | ConvertTo-Json -Depth 6))
$outputBytes = [long]$manifestBytes.Length
foreach ($entry in $staged) { $outputBytes += $entry.Bytes.Length }
if ($outputBytes -gt $MaxTotalBytes) { throw 'Normalized export exceeds the export byte budget.' }
[void][IO.Directory]::CreateDirectory($destination)
foreach ($entry in $staged) {
    $stream = [IO.FileStream]::new([IO.Path]::Combine($destination, $entry.Name), [IO.FileMode]::CreateNew)
    try { $stream.Write($entry.Bytes) } finally { $stream.Dispose() }
}
[IO.File]::WriteAllBytes([IO.Path]::Combine($destination, 'export-manifest.json'), $manifestBytes)
[ordered]@{ sessionId = $SessionId; outputDirectory = $destination; exportedEvents = $exportedEvents;
    files = $staged.Count + 1; bytes = $outputBytes; incompleteTailsDropped = $incompleteTails } | ConvertTo-Json
