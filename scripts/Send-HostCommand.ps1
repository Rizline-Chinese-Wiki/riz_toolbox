#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][ValidateSet('status','enable','disable','set-language','shutdown')][string]$Action,
    [string]$PluginId,
    [string]$Language,
    [ValidateRange(0,300)][int]$WaitSeconds = 0
)
$ErrorActionPreference='Stop'
$Action=$Action.ToLowerInvariant()
if($Action -in @('enable','disable')){
    if([string]::IsNullOrWhiteSpace($PluginId)){throw 'enable/disable requires PluginId.'}
}elseif($PSBoundParameters.ContainsKey('PluginId')){throw 'PluginId is only used by enable/disable.'}
if($Action -eq 'set-language'){
    if([string]::IsNullOrWhiteSpace($Language)){throw 'set-language requires Language.'}
}elseif($PSBoundParameters.ContainsKey('Language')){throw 'Language is only used by set-language.'}
$gameRoot=(Resolve-Path -LiteralPath $GameDirectory -ErrorAction Stop).Path
$dataRoot=Join-Path $gameRoot 'BepInEx/RizToolbox'
if(-not(Test-Path -LiteralPath $dataRoot -PathType Container)){throw 'The installed Toolbox data directory is missing.'}
$id=[Guid]::NewGuid().ToString('N')
$command=[ordered]@{id=$id;action=$Action}
if($PSBoundParameters.ContainsKey('PluginId')){$command.pluginId=$PluginId}
if($PSBoundParameters.ContainsKey('Language')){$command.language=$Language}
$target=Join-Path $dataRoot 'command.json'
$temporary=Join-Path $dataRoot ('command.'+$id+'.tmp')
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

# All instances of this sender share one mailbox. Keep the lock through the
# receipt read so another invocation cannot replace either half of the exchange.
$lockPath = Join-Path $dataRoot 'command-sender.lock'
try {
    $senderLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
} catch [IO.IOException] {
    throw "Cannot acquire the host command mailbox at '$lockPath'. No command was sent. $($_.Exception.Message)"
}
try {
    if ($WaitSeconds -gt 0) {
        $status = Read-MailboxJson $statusPath
        if ($status -isnot [Collections.IDictionary] -or $status['stopped'] -isnot [bool] -or $status['stopped'] -or
            $status['commandReceiptVersion'] -isnot [long] -or $status['commandReceiptVersion'] -ne 1 -or
            $status['diagnostics'] -isnot [Collections.IDictionary] -or $status['diagnostics']['SessionId'] -isnot [string] -or
            [string]::IsNullOrWhiteSpace($status['diagnostics']['SessionId'])) {
            throw 'Waiting requires an active host status with commandReceiptVersion 1 and a session ID. No command was sent.'
        }
        $session = $status['diagnostics']['SessionId']
        $command.expectedSessionId = $session
    }
    [IO.File]::WriteAllText($temporary,($command|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary,$target,$true)
    if ($WaitSeconds -eq 0) {
        return [pscustomobject]@{Id=$id;Action=$Action;CommandPath=$target;StatusPath=$statusPath;Dispatched=$false;
            Message='Command saved. The running host must process it on the game thread; check status.json and toolbox.log for the result.'}
    }

    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $nextStatusCheck = [TimeSpan]::FromSeconds(1)
    do {
        $receipt = Read-MailboxJson $receiptPath
        if ($receipt -is [Collections.IDictionary] -and $receipt['schemaVersion'] -is [long] -and $receipt['schemaVersion'] -eq 1 -and
            $session -ceq $receipt['sessionId'] -and $id -ceq $receipt['commandId'] -and $Action -ceq $receipt['action'] -and
            ($null -eq $receipt['pluginId'] -or $receipt['pluginId'] -is [string]) -and $command['pluginId'] -ceq $receipt['pluginId'] -and
            $receipt['outcome'] -cin @('succeeded','waiting','rejected','failed')) {
            return [pscustomobject]@{
                Id=$id; Action=$Action; CommandPath=$target; StatusPath=$statusPath; Dispatched=$true
                Outcome=$receipt.outcome; Succeeded=($receipt.outcome -ceq 'succeeded'); Receipt=$receipt
            }
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
} finally {
    try { [IO.File]::Delete($temporary) } finally { $senderLock.Dispose() }
}
