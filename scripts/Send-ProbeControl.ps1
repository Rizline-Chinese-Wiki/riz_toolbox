#requires -Version 7.2
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$GameDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('pause', 'resume', 'set-speed', 'seek')]
    [string]$Action,

    [double]$Value,

    [ValidateSet('game-seconds', 'chart-coordinates')]
    [string]$SeekUnit = 'game-seconds',

    [ValidateRange(0, 600)]
    [int]$DelaySeconds = 0,

    [ValidateRange(1, 600)]
    [int]$WaitSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ConvertFromJsonSupportsDateKind = (Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')

function Read-MailboxDocument {
    param([string]$Path, [int]$MaximumBytes)

    if (-not [System.IO.File]::Exists($Path)) { return $null }
    $length = ([System.IO.FileInfo]::new($Path)).Length
    if ($length -lt 1 -or $length -gt $MaximumBytes) {
        throw "Mailbox file has invalid length ($length bytes): $Path"
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 1 -or $bytes.Length -gt $MaximumBytes) {
        throw "Mailbox file changed size while reading: $Path"
    }
    $json = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $parseOptions = @{ InputObject = $json; AsHashtable = $true; Depth = 16 }
    if ($script:ConvertFromJsonSupportsDateKind) { $parseOptions.DateKind = 'String' }
    $value = ConvertFrom-Json @parseOptions
    if ($value -isnot [System.Collections.IDictionary]) {
        throw "Mailbox file must contain one JSON object: $Path"
    }
    # Before PowerShell 7.5, ConvertFrom-Json converts UTC strings to local DateTime.
    # Preserve the wire timestamps so the UTC marker survives on PowerShell 7.2.
    $parsed = [System.Text.Json.JsonDocument]::Parse($json)
    try {
        foreach ($name in @('updatedUtc', 'completedUtc')) {
            try { $field = $parsed.RootElement.GetProperty($name) }
            catch [System.Collections.Generic.KeyNotFoundException] { continue }
            if ($field.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
                $value[$name] = $field.GetString()
            }
        }
    }
    finally { $parsed.Dispose() }
    return [pscustomobject]@{ Object = $value; Json = $json }
}

function Test-Integer {
    param($Value)
    return ($Value -is [int] -or $Value -is [long])
}

function Test-GuidN {
    param($Value)
    return ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{32}$')
}

function Test-FreshStatus {
    param([System.Collections.IDictionary]$Status)

    foreach ($name in @('schemaVersion', 'sessionId', 'updatedUtc', 'active', 'commandsConsumed', 'maxCommands', 'state')) {
        if (-not $Status.Contains($name)) { return "Status is missing $name." }
    }
    if (-not (Test-Integer $Status['schemaVersion']) -or $Status['schemaVersion'] -ne 1) {
        return 'Status has an unsupported schemaVersion.'
    }
    if (-not (Test-GuidN $Status['sessionId'])) { return 'Status has no valid sessionId.' }
    if ($Status['active'] -isnot [bool] -or $Status['active'] -ne $true) { return 'Probe is not active.' }
    if (-not (Test-Integer $Status['commandsConsumed']) -or $Status['commandsConsumed'] -lt 0 -or
        -not (Test-Integer $Status['maxCommands']) -or $Status['maxCommands'] -ne 32 -or
        $Status['commandsConsumed'] -ge $Status['maxCommands']) {
        return 'Probe command budget is absent or exhausted.'
    }
    $state = $Status['state']
    if ($state -isnot [System.Collections.IDictionary] -or
        -not $state.Contains('controllerGeneration') -or -not $state.Contains('observedRunId') -or
        -not (Test-Integer $state['controllerGeneration']) -or $state['controllerGeneration'] -le 0 -or
        $state['observedRunId'] -isnot [string] -or
        [string]::IsNullOrWhiteSpace($state['observedRunId']) -or $state['observedRunId'].Length -gt 128) {
        return 'Status has no usable observed run and controller generation.'
    }
    $stamp = $Status['updatedUtc']
    if ($stamp -isnot [string] -or $stamp -cnotmatch '(Z|\+00:00)$') {
        return 'Status updatedUtc must be an explicit UTC timestamp.'
    }
    try {
        $updated = [System.DateTimeOffset]::Parse($stamp, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch { return 'Status updatedUtc is invalid.' }
    $age = ([System.DateTimeOffset]::UtcNow - $updated).TotalSeconds
    if ($age -lt -5 -or $age -gt 15) { return 'Status heartbeat is stale or in the future.' }
    return $null
}

function Test-CompletedResult {
    param([System.Collections.IDictionary]$Result, [string]$SessionId, [string]$Nonce, [string]$ExpectedAction)

    foreach ($name in @('schemaVersion', 'sessionId', 'nonce', 'completedUtc', 'action', 'stage', 'result')) {
        if (-not $Result.Contains($name)) { return $false }
    }
    return ((Test-Integer $Result['schemaVersion']) -and $Result['schemaVersion'] -eq 1 -and
        $Result['sessionId'] -ceq $SessionId -and $Result['nonce'] -ceq $Nonce -and
        $Result['action'] -ceq $ExpectedAction -and
        $Result['completedUtc'] -is [string] -and
        $Result['stage'] -cin @('validation', 'acquire', 'execute', 'exception') -and
        $Result['result'] -is [System.Collections.IDictionary] -and
        $Result['result'].Contains('success') -and $Result['result']['success'] -is [bool])
}

function Write-RequestAtomically {
    param([string]$Directory, [string]$Destination, [string]$Nonce, [byte[]]$Bytes)

    $temporary = [System.IO.Path]::Combine($Directory, "control-request-$Nonce.tmp")
    try {
        $stream = [System.IO.FileStream]::new($temporary, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        [System.IO.File]::Move($temporary, $Destination, $true)
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) { [System.IO.File]::Delete($temporary) }
    }
}

$actionName = $Action.ToLowerInvariant()
$hasValue = $PSBoundParameters.ContainsKey('Value')
if ($actionName -ne 'seek' -and $PSBoundParameters.ContainsKey('SeekUnit')) {
    throw '-SeekUnit is allowed only for seek.'
}
if ($actionName -in @('set-speed', 'seek')) {
    if (-not $hasValue -or [double]::IsNaN($Value) -or [double]::IsInfinity($Value) -or
        ($actionName -eq 'set-speed' -and $Value -le 0)) {
        throw 'set-speed requires a positive finite -Value. seek requires a finite -Value in the selected unit.'
    }
}
elseif ($hasValue) { throw '-Value is allowed only for set-speed and seek.' }

$game = Get-Item -LiteralPath $GameDirectory -ErrorAction Stop
if (-not $game.PSIsContainer) { throw 'GameDirectory must be an existing directory.' }
$mailboxDirectory = [System.IO.Path]::Combine($game.FullName, 'BepInEx', 'RizToolbox', 'plugin-storage',
    'storage', 'plugin-example.panel', 'data', 'runtime-module-proof')
$statusPath = [System.IO.Path]::Combine($mailboxDirectory, 'control-status.json')
$requestPath = [System.IO.Path]::Combine($mailboxDirectory, 'control-request.json')
$resultPath = [System.IO.Path]::Combine($mailboxDirectory, 'control-result.json')
$lockPath = [System.IO.Path]::Combine($mailboxDirectory, 'sender.lock')

if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }
$statusDeadline = [System.DateTimeOffset]::UtcNow.AddSeconds([Math]::Min($WaitSeconds, 60))
while (-not [System.IO.Directory]::Exists($mailboxDirectory) -and
    [System.DateTimeOffset]::UtcNow -lt $statusDeadline) {
    Start-Sleep -Milliseconds 250
}
if (-not [System.IO.Directory]::Exists($mailboxDirectory)) {
    throw "No probe mailbox directory appeared within the bounded wait: $mailboxDirectory"
}

$senderLock = $null
try {
    try {
        $senderLock = [System.IO.FileStream]::new($lockPath, [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] { throw 'Another probe sender holds sender.lock; no request was sent.' }

    $status = $null
    $statusIssue = 'Status has not appeared.'
    while ([System.DateTimeOffset]::UtcNow -lt $statusDeadline) {
        try {
            $document = Read-MailboxDocument -Path $statusPath -MaximumBytes 32768
            if ($null -ne $document) {
                $statusIssue = Test-FreshStatus -Status $document.Object
                if ($null -eq $statusIssue) { $status = $document.Object; break }
            }
        }
        catch { $statusIssue = $_.Exception.Message }
        Start-Sleep -Milliseconds 250
    }
    if ($null -eq $status) { throw "No fresh, usable probe status appeared within the bounded wait. $statusIssue No request was sent." }

    $priorRequest = Read-MailboxDocument -Path $requestPath -MaximumBytes 4096
    if ($null -ne $priorRequest) {
        $previous = $priorRequest.Object
        if (-not $previous.Contains('sessionId') -or -not $previous.Contains('nonce') -or
            -not $previous.Contains('action') -or -not (Test-GuidN $previous['sessionId']) -or
            -not (Test-GuidN $previous['nonce']) -or
            $previous['action'] -cnotin @('pause', 'resume', 'set-speed', 'seek')) {
            throw 'An existing request is not identifiable; it was not overwritten.'
        }
        $priorResult = Read-MailboxDocument -Path $resultPath -MaximumBytes 32768
        if ($null -eq $priorResult -or -not (Test-CompletedResult -Result $priorResult.Object `
            -SessionId $previous['sessionId'] -Nonce $previous['nonce'] -ExpectedAction $previous['action'])) {
            throw 'An existing request has no matching completed result; it was not overwritten.'
        }
    }

    $currentDocument = Read-MailboxDocument -Path $statusPath -MaximumBytes 32768
    $currentIssue = if ($null -eq $currentDocument) { 'Status disappeared.' } else { Test-FreshStatus -Status $currentDocument.Object }
    if ($null -ne $currentIssue) { throw "Status changed before publication: $currentIssue No request was sent." }
    $current = $currentDocument.Object
    if ($current['sessionId'] -cne $status['sessionId'] -or
        $current['state']['observedRunId'] -cne $status['state']['observedRunId'] -or
        $current['state']['controllerGeneration'] -ne $status['state']['controllerGeneration'] -or
        $current['commandsConsumed'] -ne $status['commandsConsumed']) {
        throw 'Probe session, run, generation, or command count changed before publication; no request was sent.'
    }

    $nonce = [System.Guid]::NewGuid().ToString('N')
    $request = [ordered]@{
        schemaVersion = 1
        sessionId = $status['sessionId']
        nonce = $nonce
        expectedRunId = $status['state']['observedRunId']
        expectedGeneration = $status['state']['controllerGeneration']
        action = $actionName
        expiresUtc = [System.DateTimeOffset]::UtcNow.AddSeconds(120).ToString('O', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($hasValue) {
        $request['value'] = $Value
        $request['unit'] = if ($actionName -eq 'set-speed') { 'rate' } else { $SeekUnit.ToLowerInvariant() }
    }
    $requestJson = ConvertTo-Json -InputObject $request -Compress -Depth 5
    $requestBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($requestJson)
    if ($requestBytes.Length -gt 4096) { throw 'Request exceeds 4096 bytes; no request was sent.' }
    Write-RequestAtomically -Directory $mailboxDirectory -Destination $requestPath -Nonce $nonce -Bytes $requestBytes

    $resultDeadline = [System.DateTimeOffset]::UtcNow.AddSeconds($WaitSeconds)
    while ([System.DateTimeOffset]::UtcNow -lt $resultDeadline) {
        try {
            $reply = Read-MailboxDocument -Path $resultPath -MaximumBytes 32768
            if ($null -ne $reply -and (Test-CompletedResult -Result $reply.Object `
                -SessionId $status['sessionId'] -Nonce $nonce -ExpectedAction $actionName)) {
                Write-Output $reply.Json
                return
            }
        }
        catch { } # A concurrent atomic replacement or invalid result does not authorize a resend.
        Start-Sleep -Milliseconds 250
    }
    throw "Outcome unknown: no matching result within $WaitSeconds seconds. sessionId=$($status['sessionId']) nonce=$nonce. Do not retry this request automatically."
}
finally {
    if ($null -ne $senderLock) { $senderLock.Dispose() }
}
