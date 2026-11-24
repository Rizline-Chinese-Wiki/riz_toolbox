#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
if (Get-Process -Name Rizline -ErrorAction SilentlyContinue) { throw 'Close Rizline before starting owned runtime validation.' }
& (Join-Path $PSScriptRoot 'Install.ps1') -GameDirectory $gameRoot
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Install failed.' }
$dataRoot = Join-Path $gameRoot 'BepInEx/RizToolbox'
$settingsPath = Join-Path $dataRoot 'settings.json'
$planPath = Join-Path $dataRoot 'validation-plan.json'
$manifestPath = Join-Path $dataRoot 'installation.json'
$logPath = Join-Path $dataRoot 'toolbox.log'
$diagnosticsRoot = Join-Path $dataRoot 'diagnostics'
$initialSessions = @{}
if (Test-Path -LiteralPath $diagnosticsRoot) {
    foreach ($directory in Get-ChildItem -LiteralPath $diagnosticsRoot -Directory) { $initialSessions[$directory.Name] = $true }
}
$packageVersion = (Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/package-manifest.json') -Raw | ConvertFrom-Json).version
$output = Join-Path $repoRoot ('artifacts/validation/v' + (($packageVersion -split '\.')[0..1] -join '') + '/runtime-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $output -Force | Out-Null
Write-Output "Validation evidence: $output"
$originalSettings = [IO.File]::ReadAllBytes($settingsPath)
$originalPlan = if (Test-Path -LiteralPath $planPath) { [IO.File]::ReadAllBytes($planPath) } else { $null }
$initialSettingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$settingsEntry = $manifest.files | Where-Object { $_.path.Replace('\','/') -eq 'BepInEx/RizToolbox/settings.json' }
$settingsWasOwned = $settingsEntry -and $settingsEntry.sha256 -eq $initialSettingsHash
$hashes = @{}
foreach ($name in @('Rizline.exe','GameAssembly.dll')) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $gameRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
$ownedProcess = $null
$validationSettingsHash = $null
try {
    $settings = [Text.Encoding]::UTF8.GetString($originalSettings) | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName runSelfValidation -NotePropertyValue $true -Force
    $settings | Add-Member -NotePropertyName enableExamples -NotePropertyValue $true -Force
    $settings | Add-Member -NotePropertyName disabledPlugins -NotePropertyValue @() -Force
    $settings | Add-Member -NotePropertyName validationRequiredCapabilities -NotePropertyValue @('riz.game-resources','riz.catalog','riz.scores','riz.scenes','riz.runs') -Force
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsPath -Encoding utf8
    $validationSettingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()
    @(
        @{ atSeconds=4; action='disable'; pluginId='example.color-high' },
        @{ atSeconds=6; action='disable'; pluginId='example.color-low' },
        @{ atSeconds=8; action='enable'; pluginId='example.color-high' },
        @{ atSeconds=10; action='enable'; pluginId='example.color-low' },
        @{ atSeconds=12; action='disable'; pluginId='example.color-low' },
        @{ atSeconds=14; action='disable'; pluginId='example.color-high' },
        @{ atSeconds=16; action='shutdown' }
    ) | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $planPath -Encoding utf8
    $ownedProcess = Start-Process -FilePath (Join-Path $gameRoot 'Rizline.exe') -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
    Write-Output "Owned validation game PID: $($ownedProcess.Id)"
    if (-not $ownedProcess.WaitForExit(100000)) { throw 'Owned game validation did not exit in 100 seconds.' }
    $status = Get-Content -LiteralPath (Join-Path $dataRoot 'status.json') -Raw | ConvertFrom-Json
    $newSessions = @(Get-ChildItem -LiteralPath $diagnosticsRoot -Directory | Where-Object { -not $initialSessions.ContainsKey($_.Name) })
    if ($newSessions.Count -ne 1) { throw 'Cannot uniquely attribute diagnostics to the owned validation process.' }
    $sessionId = $newSessions[0].Name
    if ($sessionId -notmatch '^[A-Za-z0-9_-]{1,80}$') { throw 'Validation diagnostic session identity is invalid.' }
    $sessionDirectory = Join-Path (Join-Path $dataRoot 'diagnostics') $sessionId
    $entries = @(Get-ChildItem -LiteralPath $sessionDirectory -Filter 'events-*.jsonl' -File | Sort-Object Name | ForEach-Object {
        Get-Content -LiteralPath $_.FullName | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace($_)) { $_ | ConvertFrom-Json } }
    })
    if (@($entries | Where-Object { $_.sessionId -ne $sessionId }).Count) { throw 'Mixed diagnostic sessions in runtime evidence.' }
    $sessionLog = ($entries | ForEach-Object {
        $level = if ($_.level -is [string]) { $_.level } else { @('Debug','Info','Warning','Error')[[int]$_.level] }
        "$($_.timestamp) [$level] $($_.message) $($_.exception)"
    }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $output 'runtime.log'), $sessionLog)
    $entries | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'events.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'Export-Diagnostics.ps1') -DiagnosticsRoot (Join-Path $dataRoot 'diagnostics') -SessionId $sessionId -OutputDirectory (Join-Path $output 'diagnostic-export')
    $summary = [ordered]@{
        version=$packageVersion; processId=$ownedProcess.Id; exitCode=$ownedProcess.ExitCode; diagnosticSessionId=$sessionId;
        isolatedPackages=(@($sessionLog -split "`n" | Where-Object { $_ -match 'Package registration .*: True' }).Count);
        mainThread=($sessionLog -match 'Unity main-thread host initialized');
        prerequisitesReady=($sessionLog -match 'Validation prerequisites ready');
        realGameResource=($sessionLog -match 'Real game resource discovered: game\.(menu|ui)/');
        twoLayers=($sessionLog -match 'Game resource read-back verified: .*layers=2; topPriority=20');
        lowerLayerRestored=($sessionLog -match 'Game resource read-back verified: .*layers=1; topPriority=10');
        originalRestored=($sessionLog -match 'Game resource read-back verified: .*layers=0; topPriority=baseline');
        highLayerSurvivesLowerRemoval=($sessionLog -match 'Game resource read-back verified: .*layers=1; topPriority=20');
        catalog=($sessionLog -match 'Game data validated: 444 charts / 444 records');
        lateScorePlugin=($sessionLog -match '\[example.scores\].*Exported 444 charts');
        eventSubscriptions=($sessionLog -match '\[example.events\].*Subscribed to game.run.finished');
        childScopeCreated=($sessionLog -match 'Created child ownership scope');
        childScopeCancelled=($sessionLog -match 'Panel child scope cancelled before resource release');
        sceneTransitionsObserved=($sessionLog -match 'Observed game event game.scene.changed');
        runStartedObserved=($sessionLog -match 'Observed game event game.run.started');
        runFinishedObserved=($sessionLog -match 'Observed game event game.run.finished');
        allStopped=($status.stopped -eq $true -and $null -ne $status.plugins -and
            @($status.shutdownFailures).Count -eq 0 -and
            @($status.plugins | Where-Object { $_.State -ne 0 }).Count -eq 0);
        runtimeErrors=(@($sessionLog -split "`n" | Where-Object { $_ -match '\[Error\]' }).Count);
        runtimeWarnings=(@($sessionLog -split "`n" | Where-Object { $_ -match '\[Warning\]' }).Count);
        detailedCapabilities=@($status.capabilityDetails).Count;
        diagnosticsDiskFailures=$status.diagnostics.DiskFailures;
        statusMatchesSession=($status.diagnostics.SessionId -eq $sessionId);
        originalHashes=$hashes
    }
    foreach ($name in $hashes.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $gameRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) { throw "Original game file changed: $name" }
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $dataRoot 'status.json') -Destination (Join-Path $output 'status.json')
    $summary | ConvertTo-Json -Depth 6 | Write-Output
    foreach ($name in @('mainThread','prerequisitesReady','realGameResource','twoLayers','lowerLayerRestored','originalRestored','highLayerSurvivesLowerRemoval','catalog','lateScorePlugin','eventSubscriptions','childScopeCreated','childScopeCancelled','allStopped','statusMatchesSession')) {
        if (-not $summary[$name]) { throw "Runtime validation failed: $name" }
    }
    if ($summary.isolatedPackages -ne 5 -or $summary.runtimeErrors -ne 0) { throw 'Package count or runtime error check failed.' }
} finally {
    if ($ownedProcess -and -not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id -ErrorAction SilentlyContinue }
    if ($validationSettingsHash -and (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $validationSettingsHash) {
        [IO.File]::WriteAllBytes($settingsPath, $originalSettings)
        if ($settingsWasOwned) {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            foreach ($entry in $manifest.files) { if ($entry.path.Replace('\','/') -eq 'BepInEx/RizToolbox/settings.json') { $entry.sha256 = $initialSettingsHash } }
            $temporaryManifest = $manifestPath + '.validation.tmp'
            $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporaryManifest -Encoding utf8
            [IO.File]::Move($temporaryManifest, $manifestPath, $true)
        }
    } else { Write-Warning 'Settings changed externally during validation; preserving the current file.' }
    if ($null -ne $originalPlan) { [IO.File]::WriteAllBytes($planPath, $originalPlan) }
    elseif (Test-Path -LiteralPath $planPath) { Remove-Item -LiteralPath $planPath }
}
