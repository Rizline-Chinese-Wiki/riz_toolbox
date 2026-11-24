#requires -Version 7.2
$ErrorActionPreference = 'Stop'
$export = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/Export-Diagnostics.ps1'))
$area = Join-Path ([IO.Path]::GetTempPath()) ('riz-diagnostic-export-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($area)
$checks = 0
function Assert-Export([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Expect-ExportFailure([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-Export $failed $Message
}
try {
    $root = Join-Path $area 'diagnostics'
    foreach ($id in @('selected','other')) {
        $session = Join-Path $root $id
        [void][IO.Directory]::CreateDirectory($session)
        @{schemaVersion=1;sessionId=$id;startedAt=[DateTimeOffset]::UtcNow} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $session 'session.json')
        @{schemaVersion=1;sessionId=$id;sequence=1;timestamp=[DateTimeOffset]::UtcNow;level='Info';eventCode='probe';message=$id;
            context=@{pluginId='probe';runId='run-1'}} | ConvertTo-Json -Depth 6 -Compress | Set-Content -LiteralPath (Join-Path $session 'events-000001.jsonl')
        'private-save' | Set-Content -LiteralPath (Join-Path $session 'save.json')
    }
    $output = Join-Path $area 'evidence'
    & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory $output | Out-Null
    Assert-Export ((Get-Content -LiteralPath (Join-Path $output 'events-000001.jsonl') -Raw) -match 'selected') 'selected session included'
    Assert-Export (-not (Test-Path -LiteralPath (Join-Path $output 'save.json'))) 'saves excluded'
    Assert-Export ((Get-ChildItem -LiteralPath $output -File).Count -eq 3) 'bounded selected file set'
    $snapshot = [ordered]@{sessionId='selected';directoryPath='private-local-path';attempted=5;written=2;textWritten=2;
        enqueued=3;processed=2;queueDropped=1;queueAbandoned=0;pendingEvents=1;pendingBytes=128;
        peakPendingEvents=2;peakPendingBytes=256;lastEnqueuedSequence=3;lastProcessedSequence=2;
        lastSuccessfulFlushSequence=2;flushTimeouts=1;pendingFlushAttempts=2;peakPendingFlushAttempts=3;
        acceptingWrites=$true;consumerCompleted=$false}
    $snapshot | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'selected/snapshot.json')
    $snapshotOutput = Join-Path $area 'snapshot-evidence'
    & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory $snapshotOutput | Out-Null
    $exportedSnapshot = Get-Content -LiteralPath (Join-Path $snapshotOutput 'snapshot.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in @('enqueued','processed','queueDropped','queueAbandoned','pendingEvents','pendingBytes',
        'peakPendingEvents','peakPendingBytes','lastEnqueuedSequence','lastProcessedSequence',
        'lastSuccessfulFlushSequence','flushTimeouts','pendingFlushAttempts','peakPendingFlushAttempts',
        'acceptingWrites','consumerCompleted')) {
        Assert-Export ($exportedSnapshot.ContainsKey($key) -and $exportedSnapshot[$key] -eq $snapshot[$key]) "queue diagnostic $key preserved"
    }
    Assert-Export (-not $exportedSnapshot.ContainsKey('directoryPath')) 'local snapshot directory excluded'
    Expect-ExportFailure { & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory $output } 'existing output preserved'
    Expect-ExportFailure { & $export -DiagnosticsRoot $root -SessionId '../other' -OutputDirectory (Join-Path $area 'escape') } 'path traversal denied'
    Expect-ExportFailure { & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory (Join-Path $root 'nested') } 'output inside source denied'
    $foreign = @{schemaVersion=1;sessionId='other';message='must reject'} | ConvertTo-Json -Compress
    $foreign | Add-Content -LiteralPath (Join-Path $root 'selected/events-000001.jsonl')
    $rejected = Join-Path $area 'rejected'
    Expect-ExportFailure { & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory $rejected } 'mixed session rejected'
    Assert-Export (-not (Test-Path -LiteralPath $rejected)) 'invalid export writes no destination'
    ('x' * 5000) | Set-Content -LiteralPath (Join-Path $root 'selected/events-000001.jsonl')
    Expect-ExportFailure { & $export -DiagnosticsRoot $root -SessionId selected -OutputDirectory (Join-Path $area 'huge') -MaxTotalBytes 4096 } 'byte budget enforced'
    Write-Output "Diagnostics export: $checks/$checks passed."
} finally {
    # The absolute directory was generated under the OS temporary root in this test only.
    $resolvedArea = [IO.Path]::GetFullPath($area)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedArea.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing unsafe test cleanup.' }
    Remove-Item -LiteralPath $resolvedArea -Recurse -Force
}
