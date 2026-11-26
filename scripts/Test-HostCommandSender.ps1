#requires -Version 7.2
[CmdletBinding()]
param([string]$SenderPath = (Join-Path $PSScriptRoot 'Send-HostCommand.ps1'))

$ErrorActionPreference = 'Stop'
$sender = (Resolve-Path -LiteralPath $SenderPath).Path
$tempRoot = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path)
$fixtureRoot = Join-Path $tempRoot ('RizToolbox.HostCommandSender-' + [guid]::NewGuid().ToString('N'))
$jobs = [Collections.Generic.List[System.Management.Automation.Job]]::new()
$passed = [Collections.Generic.List[string]]::new()
$session = [guid]::NewGuid().ToString('N')
$nextSession = [guid]::NewGuid().ToString('N')

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Pass-Scenario([string]$Name) {
    $passed.Add($Name)
    Write-Output "PASS $Name"
}

function Write-Snapshot([string]$Path, [hashtable]$Value) {
    $temporary = "$Path.fixture.tmp"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 5 -Compress))
    [IO.File]::Move($temporary, $Path, $true)
}

function New-Status($Version, $SessionId, [bool]$Stopped = $false) {
    @{ stopped = $Stopped; commandReceiptVersion = $Version; diagnostics = @{ SessionId = $SessionId } }
}

function New-Mailbox([string]$Name, [hashtable]$Status) {
    $game = Join-Path $fixtureRoot $Name
    $data = Join-Path $game 'BepInEx/RizToolbox'
    $null = [IO.Directory]::CreateDirectory($data)
    $mailbox = @{
        Game = $game
        Command = Join-Path $data 'command.json'
        Status = Join-Path $data 'status.json'
        Receipt = Join-Path $data 'command-result.json'
    }
    if ($null -ne $Status) { Write-Snapshot $mailbox.Status $Status }
    return $mailbox
}

function Start-Sender($Mailbox, [hashtable]$Options) {
    $parameters = $Options + @{ GameDirectory = $Mailbox.Game }
    $job = Start-Job -ArgumentList $sender, $parameters -ScriptBlock {
        param($Sender, $Parameters)
        $ErrorActionPreference = 'Stop'
        try {
            $result = & $Sender @Parameters
            @{ Result = $result; Error = $null; ExitCode = $LASTEXITCODE }
        } catch { @{ Result = $null; Error = $_.Exception.Message } }
    }
    $jobs.Add($job)
    return $job
}

function Receive-Sender($Job) {
    Assert-Check ($null -ne (Wait-Job -Job $Job -Timeout 15)) 'The fixture sender process did not finish.'
    $records = @(Receive-Job -Job $Job)
    Assert-Check ($Job.State -eq 'Completed' -and $records.Count -eq 1) 'The fixture sender produced an unexpected process result.'
    return $records[0]
}

function Wait-Command($Mailbox, $Job) {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while ($elapsed.Elapsed.TotalSeconds -lt 10) {
        if ([IO.File]::Exists($Mailbox.Command)) {
            return (Get-Content -LiteralPath $Mailbox.Command -Raw | ConvertFrom-Json -AsHashtable)
        }
        if ($Job.State -in @('Completed', 'Failed', 'Stopped')) {
            $record = Receive-Sender $Job
            throw "Fixture sender exited before publishing command.json. $($record.Error)"
        }
        Start-Sleep -Milliseconds 25
    }
    throw 'Fixture sender did not publish command.json.'
}

function New-Receipt($Command, [string]$Outcome) {
    @{
        schemaVersion = 1
        sessionId = $session
        commandId = $Command.id
        action = $Command.action
        pluginId = $Command.pluginId
        outcome = $Outcome
        errorMessage = "fixture $Outcome"
    }
}

function Assert-Refusal([scriptblock]$Invoke, [string]$Pattern) {
    try { $null = & $Invoke }
    catch {
        Assert-Check ($_.Exception.Message -like $Pattern) "Unexpected refusal. $($_.Exception.Message)"
        return
    }
    throw "Expected sender refusal matching '$Pattern'."
}

function Assert-TimedOut($Run, $Command) {
    Assert-Check ($null -eq $Run.Result -and $Run.Error -like "*No matching receipt for command $($Command.id) within*No retry was sent.*") "Expected command $($Command.id) to time out. $($Run.Error)"
}

function Receive-PluginCommand($Mailbox, $Job, [string]$Action, [string]$PluginId) {
    $command = Wait-Command $Mailbox $Job
    Assert-Check ($command.expectedSessionId -ceq $session -and $command.id -cmatch '^[0-9a-f]{32}$' -and
        $command.action -ceq $Action -and $command.pluginId -ceq $PluginId) "Unexpected publication for $PluginId."
    [IO.File]::Move($Mailbox.Command, "$($Mailbox.Command).$($command.id).consumed")
    return $command
}

function Receive-Batch($Job, [string[]]$PluginIds, [string[]]$Outcomes, [int]$ExitCode) {
    $run = Receive-Sender $Job
    $results = @($run.Result)
    Assert-Check ($null -eq $run.Error -and $run.ExitCode -eq $ExitCode -and $results.Count -eq $PluginIds.Count
    ) "Unexpected batch result. Exit code $($run.ExitCode), result count $($results.Count). $($run.Error)"
    for ($index = 0; $index -lt $PluginIds.Count; $index++) {
        $result = $results[$index]
        $outcome = $Outcomes[$index]
        Assert-Check ($result.PluginId -ceq $PluginIds[$index] -and $result.Outcome -ceq $outcome -and
            $result.Succeeded -is [bool] -and $result.Succeeded -eq ($outcome -ceq 'succeeded')
        ) "Expected $outcome for $($PluginIds[$index]) but got $($result.Outcome) for $($result.PluginId)."
        if ($outcome -ceq 'not-sent') {
            Assert-Check ($null -eq $result.Id -and $null -eq $result.Receipt) "The skipped plugin $($result.PluginId) has a dispatch ID or receipt."
        }
    }
    return $results
}

function Assert-BatchReceipt($Result, $Command) {
    Assert-Check ($Result.Id -ceq $Command.id -and $Result.Action -ceq $Command.action -and
        $Result.SessionId -ceq $Command.expectedSessionId -and $Result.Receipt.commandId -ceq $Command.id -and
        $Result.Receipt.sessionId -ceq $Command.expectedSessionId -and $Result.Receipt.action -ceq $Command.action -and
        $Result.Receipt.pluginId -ceq $Command.pluginId -and $Result.Receipt.outcome -ceq $Result.Outcome
    ) "The result for $($Command.pluginId) does not contain its matching receipt."
}

$active = New-Status 1 $session
try {
    $mailbox = New-Mailbox 'legacy'
    $legacy = & $sender -GameDirectory $mailbox.Game -Action enable -PluginId fixture.chart
    $command = Get-Content -LiteralPath $mailbox.Command -Raw | ConvertFrom-Json -AsHashtable
    Assert-Check ($legacy.Dispatched -is [bool] -and -not $legacy.Dispatched -and
        $legacy.CommandPath -eq $mailbox.Command -and $legacy.StatusPath -eq $mailbox.Status -and
        $legacy.Id -match '^[0-9a-f]{32}$' -and $command.id -ceq $legacy.Id -and
        $command.action -ceq 'enable' -and $command.pluginId -ceq 'fixture.chart' -and
        -not $command.ContainsKey('expectedSessionId')) 'The default invocation changed the legacy result or command payload.'
    $immediate = & $sender -GameDirectory $mailbox.Game -Action status -WaitSeconds 0
    $command = Get-Content -LiteralPath $mailbox.Command -Raw | ConvertFrom-Json -AsHashtable
    Assert-Check (-not $immediate.Dispatched -and $immediate.Id -cne $legacy.Id -and $command.id -ceq $immediate.Id -and
        $command.action -ceq 'status' -and -not $command.ContainsKey('expectedSessionId')) 'Explicit zero wait did not preserve legacy dispatch.'
    Pass-Scenario 'legacy invocation with no wait or explicit zero'

    foreach ($case in @(
        @{ Outcome = 'succeeded'; Options = @{ Action = 'STATUS' } }
        @{ Outcome = 'waiting'; Options = @{ Action = 'enable'; PluginId = 'fixture.chart' } }
        @{ Outcome = 'rejected'; Options = @{ Action = 'disable'; PluginId = 'fixture.chart' } }
        @{ Outcome = 'failed'; Options = @{ Action = 'set-language'; Language = 'zh-Hans' } }
    )) {
        $mailbox = New-Mailbox $case.Outcome $active
        $job = Start-Sender $mailbox ($case.Options + @{ WaitSeconds = 5 })
        $command = Wait-Command $mailbox $job
        Assert-Check ($command.expectedSessionId -ceq $session -and $command.action -ceq $case.Options.Action.ToLowerInvariant() -and
            $command.pluginId -ceq $case.Options.PluginId -and $command.language -ceq $case.Options.Language) 'Waited command has an unexpected payload.'
        Write-Snapshot $mailbox.Receipt (New-Receipt $command $case.Outcome)
        $run = Receive-Sender $job
        $result = $run.Result
        Assert-Check ($null -eq $run.Error -and $result.Dispatched -eq $true -and
            $result.Id -ceq $command.id -and $result.Action -ceq $command.action -and $result.Outcome -ceq $case.Outcome -and
            $result.Succeeded -eq ($case.Outcome -ceq 'succeeded') -and
            $result.Receipt.errorMessage -ceq "fixture $($case.Outcome)") "Unexpected result for $($case.Outcome). $($run.Error)"
        Pass-Scenario "matched $($case.Outcome) receipt"
    }

    foreach ($case in @(
        @{ Field = 'sessionId'; Value = $nextSession }
        @{ Field = 'commandId'; Value = [guid]::NewGuid().ToString('N') }
        @{ Field = 'pluginId'; Value = 'fixture.other' }
        @{ Field = 'action'; Value = 'disable' }
        @{ Field = 'schemaVersion'; Value = $true }
    )) {
        $mailbox = New-Mailbox "wrong-$($case.Field)" $active
        $job = Start-Sender $mailbox @{ Action = 'enable'; PluginId = 'fixture.chart'; WaitSeconds = 2 }
        $command = Wait-Command $mailbox $job
        $receipt = New-Receipt $command 'succeeded'
        $receipt[$case.Field] = $case.Value
        Write-Snapshot $mailbox.Receipt $receipt
        Assert-TimedOut (Receive-Sender $job) $command
        Pass-Scenario "ignored wrong receipt $($case.Field)"
    }

    $mailbox = New-Mailbox 'timeout' $active
    $job = Start-Sender $mailbox @{ Action = 'status'; WaitSeconds = 2 }
    $command = Wait-Command $mailbox $job
    # Model consumption by moving the only publication out of the mailbox.
    [IO.File]::Move($mailbox.Command, "$($mailbox.Command).consumed")
    Assert-TimedOut (Receive-Sender $job) $command
    Assert-Check (-not [IO.File]::Exists($mailbox.Command)) 'The timed-out command was published again.'
    Pass-Scenario 'timeout without resend'

    $mailbox = New-Mailbox 'concurrent' $active
    $job = Start-Sender $mailbox @{ Action = 'status'; WaitSeconds = 5 }
    $command = Wait-Command $mailbox $job
    $original = [IO.File]::ReadAllText($mailbox.Command)
    Assert-Refusal { & $sender -GameDirectory $mailbox.Game -Action shutdown } '*Cannot acquire the host command mailbox*No command was sent.*'
    Assert-Check ([IO.File]::ReadAllText($mailbox.Command) -ceq $original) 'Another sender overwrote the active command.'
    Write-Snapshot $mailbox.Receipt (New-Receipt $command 'succeeded')
    $run = Receive-Sender $job
    Assert-Check ($null -eq $run.Error -and $run.Result.Id -ceq $command.id -and $run.Result.Succeeded) "The first sender lost its receipt. $($run.Error)"
    $after = & $sender -GameDirectory $mailbox.Game -Action status
    Assert-Check ($after.Id -cne $command.id) 'The sender lock did not release after receipt.'
    Pass-Scenario 'concurrent sender lock and release'

    $mailbox = New-Mailbox 'restart' $active
    $job = Start-Sender $mailbox @{ Action = 'status'; WaitSeconds = 5 }
    $command = Wait-Command $mailbox $job
    [IO.File]::Move($mailbox.Command, "$($mailbox.Command).consumed")
    $receipt = New-Receipt $command 'succeeded'
    $receipt.sessionId = $nextSession
    Write-Snapshot $mailbox.Receipt $receipt
    Write-Snapshot $mailbox.Status (New-Status 1 $nextSession)
    $run = Receive-Sender $job
    Assert-Check ($null -eq $run.Result -and $run.Error -like "*The host session changed while waiting for command $($command.id)*No retry was sent.*") "The sender did not detect the host restart. $($run.Error)"
    Assert-Check (-not [IO.File]::Exists($mailbox.Command)) 'The previous command was published to the new host session.'
    Pass-Scenario 'host restart during wait'

    $pluginIds = @('fixture.chart', 'fixture.theme', 'fixture.settings')
    $mailbox = New-Mailbox 'batch-success' $active
    $job = Start-Sender $mailbox @{ Action = 'ENABLE'; PluginIds = $pluginIds }
    $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $commands = foreach ($pluginId in $pluginIds) {
        $command = Receive-PluginCommand $mailbox $job 'enable' $pluginId
        Assert-Check ($seenIds.Add($command.id)) "The batch reused command ID $($command.id)."
        if ($pluginId -ceq $pluginIds[1]) {
            Assert-Refusal { & $sender -GameDirectory $mailbox.Game -Action status } '*Cannot acquire the host command mailbox*No command was sent.*'
            $staleReceipt = New-Receipt $command 'succeeded'
            $staleReceipt.commandId = [guid]::NewGuid().ToString('N')
            Write-Snapshot $mailbox.Receipt $staleReceipt
        }
        Start-Sleep -Milliseconds 250
        Assert-Check (-not [IO.File]::Exists($mailbox.Command)) "The batch published another command before the receipt for $pluginId."
        Write-Snapshot $mailbox.Receipt (New-Receipt $command 'succeeded')
        $command
    }
    $results = Receive-Batch $job $pluginIds (@('succeeded') * $pluginIds.Count) 0
    for ($index = 0; $index -lt $commands.Count; $index++) {
        Assert-BatchReceipt $results[$index] $commands[$index]
    }
    Assert-Check (-not [IO.File]::Exists($mailbox.Command)) 'The batch published more than the three requested commands.'
    Pass-Scenario 'batch published three correlated commands in one session and held the sender lock'

    foreach ($outcome in @('waiting', 'rejected', 'failed')) {
        $mailbox = New-Mailbox "batch-$outcome" $active
        $job = Start-Sender $mailbox @{ Action = 'disable'; PluginIds = ($pluginIds -join ','); WaitSeconds = 5 }
        $first = Receive-PluginCommand $mailbox $job 'disable' $pluginIds[0]
        Write-Snapshot $mailbox.Receipt (New-Receipt $first 'succeeded')
        $second = Receive-PluginCommand $mailbox $job 'disable' $pluginIds[1]
        Write-Snapshot $mailbox.Receipt (New-Receipt $second $outcome)
        $results = Receive-Batch $job $pluginIds @('succeeded', $outcome, 'not-sent') 1
        Assert-BatchReceipt $results[0] $first
        Assert-BatchReceipt $results[1] $second
        Assert-Check (-not [IO.File]::Exists($mailbox.Command)) "The batch published the third command after $outcome."
        Pass-Scenario "batch stopped after $outcome"
    }

    foreach ($case in @(
        @{ Name = 'timeout'; WaitSeconds = 2; Error = 'No matching receipt' }
        @{ Name = 'restart during wait'; WaitSeconds = 5; Error = 'session changed'; Restart = $true }
    )) {
        $mailbox = New-Mailbox "batch-$($case.Name.Replace(' ', '-'))" $active
        $job = Start-Sender $mailbox @{ Action = 'enable'; PluginIds = $pluginIds; WaitSeconds = $case.WaitSeconds }
        $command = Receive-PluginCommand $mailbox $job 'enable' $pluginIds[0]
        if ($case.Restart) {
            $receipt = New-Receipt $command 'succeeded'
            $receipt.sessionId = $nextSession
            Write-Snapshot $mailbox.Receipt $receipt
            Write-Snapshot $mailbox.Status (New-Status 1 $nextSession)
        }
        $results = Receive-Batch $job $pluginIds @('unknown', 'not-sent', 'not-sent') 1
        Assert-Check ($results[0].Id -ceq $command.id -and $results[0].SessionId -ceq $session -and
            $null -eq $results[0].Receipt -and
            $results[0].Message -like "*$($case.Error)*") "The batch did not report an unknown outcome after $($case.Name)."
        Assert-Check (-not [IO.File]::Exists($mailbox.Command)) "The batch retried or continued after $($case.Name)."
        Pass-Scenario "batch stopped after $($case.Name)"
    }

    $mailbox = New-Mailbox 'batch-restart-before-next-publication' $active
    $job = Start-Sender $mailbox @{ Action = 'enable'; PluginIds = $pluginIds; WaitSeconds = 5 }
    $command = Receive-PluginCommand $mailbox $job 'enable' $pluginIds[0]
    Write-Snapshot $mailbox.Status (New-Status 1 $nextSession)
    Write-Snapshot $mailbox.Receipt (New-Receipt $command 'succeeded')
    $results = Receive-Batch $job $pluginIds @('succeeded', 'not-sent', 'not-sent') 1
    Assert-BatchReceipt $results[0] $command
    Assert-Check (-not [IO.File]::Exists($mailbox.Command)) 'The batch published to the new session after acknowledging an old-session receipt.'
    Pass-Scenario 'batch checked its session before publishing the next command'

    $mailbox = New-Mailbox 'batch-zero-wait' $active
    Assert-Refusal { & $sender -GameDirectory $mailbox.Game -Action enable -PluginIds $pluginIds -WaitSeconds 0 } '*WaitSeconds*'
    Assert-Check (-not [IO.File]::Exists($mailbox.Command)) 'A batch with an explicit zero wait wrote command.json.'
    Pass-Scenario 'batch refused an explicit zero wait before publication'

    $unsupported = [ordered]@{
        'missing-status' = $null
        'legacy-host' = @{ stopped = $false; diagnostics = @{ SessionId = $session } }
        'wrong-version' = New-Status 2 $session
        'boolean-version' = New-Status $true $session
        'string-version' = New-Status '1' $session
        'missing-session' = New-Status 1 $null
        'non-string-session' = New-Status 1 37
        'stopped-host' = New-Status 1 $session $true
    }
    foreach ($case in $unsupported.GetEnumerator()) {
        $mailbox = New-Mailbox $case.Key $case.Value
        Assert-Refusal { & $sender -GameDirectory $mailbox.Game -Action status -WaitSeconds 2 } '*Waiting requires an active host status*No command was sent.*'
        Assert-Check (-not [IO.File]::Exists($mailbox.Command)) "$($case.Key) unexpectedly wrote command.json."
        Pass-Scenario "refused $($case.Key) before publication"
    }

    Write-Output "$($passed.Count) host command sender scenarios passed."
} finally {
    foreach ($job in $jobs) { Remove-Job -Job $job -Force }
    if ([IO.Directory]::Exists($fixtureRoot)) {
        $resolved = (Resolve-Path -LiteralPath $fixtureRoot).Path
        Assert-Check ($resolved -ceq $fixtureRoot -and [IO.Directory]::GetParent($resolved).FullName -ceq $tempRoot) 'Unexpected test cleanup target.'
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
