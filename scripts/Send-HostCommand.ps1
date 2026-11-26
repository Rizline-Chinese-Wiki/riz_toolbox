#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][ValidateSet('status','enable','disable','set-language','shutdown')][string]$Action,
    [string]$PluginId,
    [string]$Language,
    [ValidateRange(0,300)][int]$WaitSeconds = 0,
    [string[]]$PluginIds
)
$ErrorActionPreference='Stop'
$Action=$Action.ToLowerInvariant()
$batch = $PSBoundParameters.ContainsKey('PluginIds')
if ($batch) {
    if ($Action -notin @('enable', 'disable')) { throw 'PluginIds is only used by enable/disable.' }
    if ($PSBoundParameters.ContainsKey('PluginId')) { throw 'Specify either PluginId or PluginIds.' }
    $requestedPluginIds = @($PluginIds -split ',' | ForEach-Object { ([string]$_).Trim() })
    if ($requestedPluginIds.Count -eq 0 -or $requestedPluginIds -contains '') {
        throw 'PluginIds requires one or more non-empty plugin IDs.'
    }
    if (-not $PSBoundParameters.ContainsKey('WaitSeconds')) { $WaitSeconds = 10 }
    if ($WaitSeconds -eq 0) { throw 'Batch commands require WaitSeconds between 1 and 300. No command was sent.' }
} elseif ($Action -in @('enable', 'disable')) {
    if ([string]::IsNullOrWhiteSpace($PluginId)) { throw 'enable/disable requires PluginId.' }
} elseif ($PSBoundParameters.ContainsKey('PluginId')) {
    throw 'PluginId is only used by enable/disable.'
}
if($Action -eq 'set-language'){
    if([string]::IsNullOrWhiteSpace($Language)){throw 'set-language requires Language.'}
}elseif($PSBoundParameters.ContainsKey('Language')){throw 'Language is only used by set-language.'}
$gameRoot=(Resolve-Path -LiteralPath $GameDirectory -ErrorAction Stop).Path
$dataRoot=Join-Path $gameRoot 'BepInEx/RizToolbox'
if(-not(Test-Path -LiteralPath $dataRoot -PathType Container)){throw 'The installed Toolbox data directory is missing.'}
$target=Join-Path $dataRoot 'command.json'
$statusPath=Join-Path $dataRoot 'status.json'
$receiptPath=Join-Path $dataRoot 'command-result.json'

function Read-MailboxJson([string]$Path) {
    # Allow the host to replace its snapshot while this process reads the old one.
    try {
        $stream = [IO.File]::Open($Path, 'Open', 'Read', [IO.FileShare]'ReadWrite, Delete')
    } catch [IO.FileNotFoundException] { return $null }
    try {
        $reader = [IO.StreamReader]::new($stream)
        try { return ($reader.ReadToEnd() | ConvertFrom-Json -AsHashtable -ErrorAction Stop) }
        finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-ActiveMailboxSession {
    $status = Read-MailboxJson $statusPath
    if ($status -isnot [Collections.IDictionary] -or $status['stopped'] -isnot [bool] -or $status['stopped'] -or
        $status['commandReceiptVersion'] -isnot [long] -or $status['commandReceiptVersion'] -ne 1 -or
        $status['diagnostics'] -isnot [Collections.IDictionary] -or $status['diagnostics']['SessionId'] -isnot [string] -or
        [string]::IsNullOrWhiteSpace($status['diagnostics']['SessionId'])) {
        throw 'Waiting requires an active host status with commandReceiptVersion 1 and a session ID. No command was sent.'
    }
    return $status['diagnostics']['SessionId']
}

function New-MailboxCommand([string]$RequestedPlugin, [string]$SessionId) {
    $command = [ordered]@{ id = [Guid]::NewGuid().ToString('N'); action = $Action }
    if ($Action -in @('enable', 'disable')) { $command.pluginId = $RequestedPlugin }
    if ($Action -eq 'set-language') { $command.language = $Language }
    if ($SessionId) { $command.expectedSessionId = $SessionId }
    return $command
}

function Write-MailboxCommand([Collections.IDictionary]$Command) {
    $temporary = Join-Path $dataRoot ('command.' + $Command['id'] + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, ($Command | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $target, $true)
    } catch {
        [IO.File]::Delete($temporary)
        throw
    }
}

function Wait-MailboxReceipt([Collections.IDictionary]$Command) {
    $id = $Command['id']
    $session = $Command['expectedSessionId']
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $nextStatusCheck = [TimeSpan]::FromSeconds(1)
    do {
        $receipt = Read-MailboxJson $receiptPath
        if ($receipt -is [Collections.IDictionary] -and $receipt['schemaVersion'] -is [long] -and $receipt['schemaVersion'] -eq 1 -and
            $session -ceq $receipt['sessionId'] -and $id -ceq $receipt['commandId'] -and $Command['action'] -ceq $receipt['action'] -and
            ($null -eq $receipt['pluginId'] -or $receipt['pluginId'] -is [string]) -and $Command['pluginId'] -ceq $receipt['pluginId'] -and
            $receipt['outcome'] -cin @('succeeded','waiting','rejected','failed')) {
            return $receipt
        }
        if ($elapsed.Elapsed -ge $nextStatusCheck) {
            $latest = Read-MailboxJson $statusPath
            if ($latest -is [Collections.IDictionary] -and $latest['diagnostics'] -is [Collections.IDictionary] -and
                $latest['diagnostics']['SessionId'] -and $latest['diagnostics']['SessionId'] -cne $session) {
                throw "The host session changed while waiting for command $id. Its outcome is unknown. No retry was sent."
            }
            $nextStatusCheck = $elapsed.Elapsed.Add([TimeSpan]::FromSeconds(1))
        }
        Start-Sleep -Milliseconds 100
    } while ($elapsed.Elapsed.TotalSeconds -lt $WaitSeconds)
    throw "No matching receipt for command $id within $WaitSeconds seconds. Its outcome is unknown. No retry was sent."
}

# Hold the sender lock across all writes and receipt reads in this invocation.
$lockPath = Join-Path $dataRoot 'command-sender.lock'
try {
    $senderLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
} catch [IO.IOException] {
    throw "Cannot acquire the host command mailbox at '$lockPath'. No command was sent. $($_.Exception.Message)"
}

try {
    $session = $null
    if (-not $batch) {
        if ($WaitSeconds -gt 0) { $session = Get-ActiveMailboxSession }
        $command = New-MailboxCommand $PluginId $session
        $id = $command['id']
        Write-MailboxCommand $command
        if ($WaitSeconds -eq 0) {
            return [pscustomobject]@{Id=$id;Action=$Action;CommandPath=$target;StatusPath=$statusPath;Dispatched=$false;
                Message='Command saved. The running host must process it on the game thread; check status.json and toolbox.log for the result.'}
        }
        $receipt = Wait-MailboxReceipt $command
        return [pscustomobject]@{
            Id=$id; Action=$Action; CommandPath=$target; StatusPath=$statusPath; Dispatched=$true
            Outcome=$receipt.outcome; Succeeded=($receipt.outcome -ceq 'succeeded'); Receipt=$receipt
        }
    }

    $batchStopped = $false
    $batchResults = foreach ($requestedPlugin in $requestedPluginIds) {
        $id = $null
        $receipt = $null
        $outcome = 'not-sent'
        if ($batchStopped) {
            $message = 'The batch stopped at an earlier plugin. No command was sent.'
        } else {
            try {
                $currentSession = Get-ActiveMailboxSession
                if ($null -eq $session) {
                    $session = $currentSession
                } elseif ($currentSession -cne $session) {
                    throw "The host session changed before sending a command for '$requestedPlugin'. No command was sent."
                }
                $command = New-MailboxCommand $requestedPlugin $session
                Write-MailboxCommand $command
                $id = $command['id']
                $outcome = 'unknown'
                $receipt = Wait-MailboxReceipt $command
                $outcome = $receipt['outcome']
                $message = $receipt['errorMessage']
            } catch {
                $message = $_.Exception.Message
            }
            $batchStopped = $outcome -cne 'succeeded'
        }
        [pscustomobject]@{
            PluginId = $requestedPlugin
            Id = $id
            Action = $Action
            SessionId = $session
            Outcome = $outcome
            Succeeded = $outcome -ceq 'succeeded'
            Receipt = $receipt
            Message = $message
        }
    }
} finally {
    $senderLock.Dispose()
}

$batchResults
if ($batchStopped) { exit 1 }
exit 0
