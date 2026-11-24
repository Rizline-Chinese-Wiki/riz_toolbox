#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$EvidenceDirectory,
    [string]$Case = '*'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$testRoot = Join-Path $repoRoot ('artifacts\install-recovery-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$results = [Collections.Generic.List[object]]::new()
$global:RizInstallRecoveryFailCopySource = $null
$global:RizInstallRecoveryBeforeCopyFailure = $null
$global:RizInstallRecoveryPretendRunning = $false
$script:InterruptedTemplate = $null

function Write-FixtureFile([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Copy-Item {
    [CmdletBinding()]
    param([string]$LiteralPath, [string]$Destination)
    if ($global:RizInstallRecoveryFailCopySource -and $LiteralPath -eq $global:RizInstallRecoveryFailCopySource) {
        if ($null -ne $global:RizInstallRecoveryBeforeCopyFailure) { & $global:RizInstallRecoveryBeforeCopyFailure }
        throw 'Injected source-copy failure.'
    }
    Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters
}
function Get-Process {
    [CmdletBinding()]
    param([string]$Name)
    if ($global:RizInstallRecoveryPretendRunning -and $Name -eq 'Rizline') { return [pscustomobject]@{Name='Rizline';Id=-1} }
    Microsoft.PowerShell.Management\Get-Process @PSBoundParameters
}
function Package([string]$Root, [string]$Version) {
    $paths = @('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader') |
        ForEach-Object { "BepInEx/plugins/RizToolbox/$_.dll" }
    $paths += 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json', 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
    $paths += 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
    $paths += 'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll'
    $paths += 'BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll'
    # This controlled fixture deliberately replaces winhttp before the SDK
    # interruption point. It tests recovery, not the production sort order.
    $paths = @('winhttp.dll','.doorstop_version','BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip','BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt') + $paths
    foreach ($id in @('example.panel','example.events','example.color-low','example.color-high','example.scores')) {
        $paths += "BepInEx/RizToolbox/mods/$id/plugin.json", "BepInEx/RizToolbox/mods/$id/RizToolbox.Examples.dll"
    }
    $files = foreach ($path in $paths) {
        $source = Join-Path $Root "artifacts/package/$path"
        Write-FixtureFile $source "$Version payload $path"
        [pscustomobject]@{ path=$path; sha256=(Get-InstallHash $source) }
    }
    @{schemaVersion=2;files=@($files)} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $Root 'artifacts/package-manifest.json') -Encoding utf8
    foreach ($path in @('winhttp.dll','.doorstop_version','BepInEx/core/dobby.dll','dotnet/runtime.dll')) {
        Write-FixtureFile (Join-Path $Root ".tools/bepinex-789/$path") "$Version loader $path"
    }
    Write-FixtureFile (Join-Path $Root '.tools/bepinex-789/doorstop_config.ini') "target_assembly = original`nfixture = $Version"
}
function New-Fixture([string]$Name) {
    $root = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts') -Force | Out-Null
    foreach ($name in @('Install.ps1','Install.Common.ps1','Uninstall.ps1')) {
        Microsoft.PowerShell.Management\Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $root "scripts/$name")
    }
    Write-FixtureFile (Join-Path $root 'scripts/Prepare-Dependencies.ps1') '# Synthetic pinned dependency fixture; no network or real game paths.'
    $game = Join-Path $root 'fake-game'
    Write-FixtureFile (Join-Path $game 'Rizline.exe') 'synthetic executable; never launched'
    Write-FixtureFile (Join-Path $game 'GameAssembly.dll') 'synthetic original game bytes'
    @{supportedBuild=@{gameAssemblySha256=(Get-InstallHash (Join-Path $game 'GameAssembly.dll'))}} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'dependencies.lock.json') -Encoding utf8
    Package $root 'v1'
    return [pscustomobject]@{ Root=$root; Game=$game; Manifest=(Join-Path $game 'BepInEx/RizToolbox/installation.json') }
}
function Install-Fixture($Fixture) { & (Join-Path $Fixture.Root 'scripts/Install.ps1') -GameDirectory $Fixture.Game | Out-Null }
function Uninstall-Fixture($Fixture) { & (Join-Path $Fixture.Root 'scripts/Uninstall.ps1') -GameDirectory $Fixture.Game | Out-Null }
function Snapshot($Fixture) {
    $manifest = Get-Content -LiteralPath $Fixture.Manifest -Raw | ConvertFrom-Json
    $map = @{}; foreach ($entry in $manifest.files) { $map[$entry.path] = Get-InstallHash (Join-Path $Fixture.Game $entry.path) }
    return $map
}
function Assert-Snapshot($Fixture, [hashtable]$Expected) {
    foreach ($path in $Expected.Keys) {
        Check ((Get-InstallHash (Join-Path $Fixture.Game $path)) -eq $Expected[$path]) "Known-good file was not restored: $path"
    }
}
function Expect-Failure([scriptblock]$Action, [string]$Contains) {
    $errorMessage = $null
    try { & $Action } catch { $errorMessage = $_.Exception.Message }
    Check ($null -ne $errorMessage -and $errorMessage.Contains($Contains)) "Expected '$Contains', got '$errorMessage'."
}
function Test-Case([string]$Name, [scriptblock]$Action) {
    if ($Case -ne '*' -and $Case -ne $Name) { return }
    try { & $Action; $results.Add([pscustomobject]@{name=$Name;passed=$true}); Write-Output "PASS $Name" }
    catch { $results.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message}); Write-Output "FAIL $Name : $($_.Exception.Message)" }
    finally {
        $global:RizInstallRecoveryFailCopySource = $null
        $global:RizInstallRecoveryBeforeCopyFailure = $null
        $global:RizInstallRecoveryPretendRunning = $false
    }
}

function Interrupt-Install($Fixture, [string]$StopBeforeSource) {
    $marker = Join-Path $Fixture.Root ('interrupt-' + [Guid]::NewGuid().ToString('N') + '.marker')
    # A separate real pwsh process is killed while blocked immediately before
    # copying a selected file. Its finally blocks cannot perform rollback.
    $worker = @'
$ErrorActionPreference = 'Stop'
function Copy-Item {
    [CmdletBinding()]
    param([string]$LiteralPath, [string]$Destination)
    if ($LiteralPath -eq '__SOURCE__') {
        [IO.File]::WriteAllText('__MARKER__', 'blocked before selected copy')
        while ($true) { Start-Sleep -Milliseconds 100 }
    }
    Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters
}
& '__INSTALL__' -GameDirectory '__GAME__'
'@
    foreach ($pair in @(
        @('__SOURCE__',$StopBeforeSource), @('__MARKER__',$marker),
        @('__INSTALL__',(Join-Path $Fixture.Root 'scripts/Install.ps1')), @('__GAME__',$Fixture.Game))) {
        $worker = $worker.Replace($pair[0], $pair[1].Replace("'", "''"))
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($worker))
    $process = Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList @('-NoProfile','-EncodedCommand',$encoded) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput ($marker + '.out') -RedirectStandardError ($marker + '.err')
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not (Test-Path -LiteralPath $marker) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        Check (Test-Path -LiteralPath $marker) "Fixture child did not reach selected copy; inspect $marker.err"
        Stop-Process -Id $process.Id -Force
        Check ($process.WaitForExit(10000)) 'Fixture child did not stop.'
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        $process.Dispose()
    }
}
function New-CrashedFixture([string]$Name) {
    if ($null -eq $script:InterruptedTemplate) {
        $template = New-Fixture 'interruption-template'; Install-Fixture $template
        Write-FixtureFile (Join-Path $template.Game 'BepInEx/RizToolbox/settings.json') '{"user":true,"keep":"original config bytes"}'
        $before = Snapshot $template
        Package $template.Root 'v2'
        Interrupt-Install $template (Join-Path $template.Root 'artifacts/package/BepInEx/plugins/RizToolbox/RizToolbox.Sdk.dll')
        $journal = Get-Content -LiteralPath $template.Manifest -Raw | ConvertFrom-Json
        Check ($journal.PSObject.Properties.Name -contains 'transaction') 'Hard interruption did not leave a transaction journal.'
        Check ((Get-InstallHash (Join-Path $template.Game 'winhttp.dll')) -ne $before['winhttp.dll']) 'Selected interruption did not occur after any real replacement.'
        $template | Add-Member -NotePropertyName Before -NotePropertyValue $before
        $script:InterruptedTemplate = $template
    }
    $source = $script:InterruptedTemplate
    $root = Join-Path $testRoot $Name
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath $source.Root -Destination $root -Recurse
    $f = [pscustomobject]@{Root=$root;Game=(Join-Path $root 'fake-game');Manifest=(Join-Path $root 'fake-game/BepInEx/RizToolbox/installation.json');Before=$source.Before}
    $journal = Get-Content -LiteralPath $f.Manifest -Raw | ConvertFrom-Json
    $journal.gameRoot = $f.Game; Write-InstallManifest $f.Manifest $journal
    return $f
}
function Read-RawManifest($Fixture) { return Get-Content -LiteralPath $Fixture.Manifest -Raw | ConvertFrom-Json }
function Current-JournalTargets($Fixture) {
    $hashes = @{}
    foreach ($entry in (Read-RawManifest $Fixture).pending) {
        $path = Join-Path $Fixture.Game $entry.path
        if (Test-Path -LiteralPath $path -PathType Leaf) { $hashes[$entry.path] = Get-InstallHash $path }
    }
    return $hashes
}

Test-Case 'failed-upgrade-restores-known-good' {
    $f = New-Fixture 'failed-upgrade'; Install-Fixture $f
    $before = Snapshot $f
    $settings = Join-Path $f.Game 'BepInEx/RizToolbox/settings.json'
    Write-FixtureFile $settings '{"custom":"preserve exactly","enableExamples":false}'
    $settingsHash = Get-InstallHash $settings
    $before.Remove('BepInEx\RizToolbox\settings.json')
    Package $f.Root 'v2'
    $global:RizInstallRecoveryFailCopySource = Join-Path $f.Root 'artifacts/package/BepInEx/plugins/RizToolbox/RizToolbox.Sdk.dll'
    Expect-Failure { Install-Fixture $f } 'Injected source-copy failure'
    Assert-Snapshot $f $before
    Check ((Get-InstallHash $settings) -eq $settingsHash) 'Failure changed user configuration.'
    $manifest = Get-Content -LiteralPath $f.Manifest -Raw | ConvertFrom-Json
    Check (@($manifest.pending).Count -eq 0) 'Successful rollback left an active pending journal.'
}

Test-Case 'normal-upgrade-keeps-user-config-and-game' {
    $f = New-Fixture 'normal-upgrade'; Install-Fixture $f
    $original = Get-InstallHash (Join-Path $f.Game 'GameAssembly.dll')
    $old = Get-InstallHash (Join-Path $f.Game 'winhttp.dll')
    $settings = Join-Path $f.Game 'BepInEx/RizToolbox/settings.json'
    Write-FixtureFile $settings '{"custom":"existing user configuration"}'; $configHash = Get-InstallHash $settings
    Package $f.Root 'v2'; Install-Fixture $f; Install-Fixture $f
    $manifest = Read-RawManifest $f
    foreach ($entry in $manifest.files) {
        if ($entry.path -ne 'BepInEx\RizToolbox\settings.json') {
            Check ((Get-InstallHash (Join-Path $f.Game $entry.path)) -eq $entry.sha256) "Upgrade hash mismatch: $($entry.path)"
        }
    }
    Check ((Get-InstallHash (Join-Path $f.Game 'winhttp.dll')) -ne $old) 'Upgrade did not replace old payload.'
    Check ((Get-InstallHash $settings) -eq $configHash -and (Get-InstallHash (Join-Path $f.Game 'GameAssembly.dll')) -eq $original) 'Upgrade changed configuration or original game.'
    Check (@($manifest.pending).Count -eq 0 -and $manifest.PSObject.Properties.Name -notcontains 'transaction') 'Commit left a live journal.'
}

Test-Case 'failed-first-install-removes-only-owned-writes' {
    $f = New-Fixture 'failed-first-install'
    $original = Get-InstallHash (Join-Path $f.Game 'GameAssembly.dll')
    $global:RizInstallRecoveryFailCopySource = Join-Path $f.Root 'artifacts/package/BepInEx/plugins/RizToolbox/RizToolbox.Sdk.dll'
    $extra = Join-Path $f.Game 'BepInEx/RizToolbox/user-data.txt'
    $global:RizInstallRecoveryBeforeCopyFailure = { [IO.File]::WriteAllText($extra, 'unrelated generated user data') }.GetNewClosure()
    Expect-Failure { Install-Fixture $f } 'Injected source-copy failure'
    Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'winhttp.dll'))) 'First-install rollback retained its new loader.'
    Check ((Read-RawManifest $f).files.Count -eq 0 -and (Read-RawManifest $f).pending.Count -eq 0) 'First-install rollback adopted partial files.'
    Check ([IO.File]::ReadAllText($extra) -eq 'unrelated generated user data') 'Rollback deleted unowned data.'
    Check ((Get-InstallHash (Join-Path $f.Game 'GameAssembly.dll')) -eq $original) 'Rollback changed original game.'
}

Test-Case 'hard-interruption-recovers-before-package-validation-and-is-idempotent' {
    $f = New-CrashedFixture 'interrupted-no-package'
    $beforeOwnership = @((Read-RawManifest $f).files | ForEach-Object { "$($_.path):$($_.sha256)" })
    Remove-Item -LiteralPath (Join-Path $f.Root 'artifacts/package-manifest.json')
    Expect-Failure { Install-Fixture $f } 'Run scripts/Build.ps1 first'
    Assert-Snapshot $f $f.Before
    $manifest = Read-RawManifest $f
    Check (($beforeOwnership -join "`n") -eq (@($manifest.files | ForEach-Object { "$($_.path):$($_.sha256)" }) -join "`n")) 'Recovery adopted mixed/new ownership.'
    Check ($manifest.pending.Count -eq 0 -and $manifest.PSObject.Properties.Name -notcontains 'transaction') 'Recovery did not finalize previous ownership.'
    Expect-Failure { Install-Fixture $f } 'Run scripts/Build.ps1 first'
    Assert-Snapshot $f $f.Before
}

Test-Case 'interrupted-rollback-retries-without-losing-old-version' {
    $f = New-CrashedFixture 'interrupted-rollback'
    $journal = Read-RawManifest $f
    $replaced = @($journal.pending | Where-Object {
        $_.previousSha256 -and (Test-Path -LiteralPath (Join-Path $f.Game $_.path)) -and
        (Get-InstallHash (Join-Path $f.Game $_.path)) -eq $_.sha256 -and $_.sha256 -ne $_.previousSha256
    })
    Check ($replaced.Count -gt 0) 'The interrupted fixture has no actually replaced target to recover.'
    $selected = $replaced[0]
    $index = [array]::IndexOf(@($journal.pending), $selected)
    Check ($index -ge 0) 'The actual replaced row is missing from the transaction journal.'
    $backup = Get-InstallTarget $f.Game (Get-InstallBackupRelative $journal.transaction.id $index)
    Interrupt-Install $f $backup
    Check ((Read-RawManifest $f).PSObject.Properties.Name -contains 'transaction') 'Partial recovery cleared its journal.'
    Remove-Item -LiteralPath (Join-Path $f.Root 'artifacts/package-manifest.json')
    Expect-Failure { Install-Fixture $f } 'Run scripts/Build.ps1 first'
    Assert-Snapshot $f $f.Before
}

Test-Case 'modified-target-refuses-recovery-with-backups-intact' {
    $f = New-CrashedFixture 'modified-recovery'
    Write-FixtureFile (Join-Path $f.Game 'winhttp.dll') 'user or third-party modified these bytes'
    $current = Current-JournalTargets $f; $journalHash = Get-InstallHash $f.Manifest
    Expect-Failure { Install-Fixture $f } 'modified'
    Assert-Snapshot $f $current
    Check ((Get-InstallHash $f.Manifest) -eq $journalHash) 'Refused recovery rewrote its journal.'
    $journal = Read-RawManifest $f
    for ($i = 0; $i -lt $journal.pending.Count; ++$i) {
        if ($journal.pending[$i].previousSha256) {
            Check ((Get-InstallHash (Get-InstallTarget $f.Game (Get-InstallBackupRelative $journal.transaction.id $i))) -eq $journal.pending[$i].previousSha256) 'Refused recovery damaged a known-good backup.'
        }
    }
}

Test-Case 'corrupt-backup-refuses-before-any-rollback-write' {
    $f = New-CrashedFixture 'corrupt-backup'; $journal = Read-RawManifest $f
    Write-FixtureFile (Get-InstallTarget $f.Game (Get-InstallBackupRelative $journal.transaction.id 0)) 'corrupt backup'
    $current = Current-JournalTargets $f; $journalHash = Get-InstallHash $f.Manifest
    Expect-Failure { Uninstall-Fixture $f } 'backup hash mismatch'
    Assert-Snapshot $f $current
    Check ((Get-InstallHash $f.Manifest) -eq $journalHash) 'Corrupt-backup refusal changed journal.'
}

Test-Case 'missing-backup-refuses-before-any-rollback-write' {
    $f = New-CrashedFixture 'missing-backup'; $journal = Read-RawManifest $f
    Remove-Item -LiteralPath (Get-InstallTarget $f.Game (Get-InstallBackupRelative $journal.transaction.id 0))
    $current = Current-JournalTargets $f
    Expect-Failure { Install-Fixture $f } 'Expected a regular file'
    Assert-Snapshot $f $current
    Check ((Read-RawManifest $f).PSObject.Properties.Name -contains 'transaction') 'Missing-backup refusal cleared journal.'
}

Test-Case 'recovery-refuses-path-escape-and-management-ownership' {
    foreach ($bad in @('..\escape.dll','BepInEx\RizToolbox\installation.json','BepInEx/RizToolbox/install-recovery/claimed.txt')) {
        $f = New-CrashedFixture ('bad-path-' + [Guid]::NewGuid().ToString('N'))
        $current = Current-JournalTargets $f; $journal = Read-RawManifest $f
        $journal.pending[0].path = $bad; Write-InstallManifest $f.Manifest $journal
        Expect-Failure { Uninstall-Fixture $f } $(if ($bad.StartsWith('..')) {'Unsafe manifest path'} else {'installer management files'})
        Assert-Snapshot $f $current
    }
}

Test-Case 'linked-backup-directory-refuses-without-touching-outside-target' {
    $f = New-CrashedFixture 'linked-backup'; $journal = Read-RawManifest $f
    $directory = Get-InstallTarget $f.Game ('BepInEx\RizToolbox\install-recovery\' + $journal.transaction.id)
    $saved = $directory + '.saved'
    foreach ($path in @($directory,$saved)) {
        Check ([IO.Path]::GetFullPath($path).StartsWith([IO.Path]::GetFullPath($testRoot) + '\',[StringComparison]::OrdinalIgnoreCase)) 'Fixture move escaped test workspace.'
    }
    Move-Item -LiteralPath $directory -Destination $saved
    $outside = Join-Path $f.Root 'outside-game'
    Write-FixtureFile (Join-Path $outside 'canary.txt') 'do not touch'
    New-Item -ItemType Junction -Path $directory -Target $outside | Out-Null
    $current = Current-JournalTargets $f
    Expect-Failure { Install-Fixture $f } 'linked installation path'
    Assert-Snapshot $f $current
    Check ([IO.File]::ReadAllText((Join-Path $outside 'canary.txt')) -eq 'do not touch') 'Recovery touched junction destination.'
}

Test-Case 'running-game-refuses-before-recovery' {
    $f = New-CrashedFixture 'running-guard'; $current = Current-JournalTargets $f; $journalHash = Get-InstallHash $f.Manifest
    $global:RizInstallRecoveryPretendRunning = $true
    Expect-Failure { Install-Fixture $f } 'Close Rizline'
    Expect-Failure { Uninstall-Fixture $f } 'Close Rizline'
    Assert-Snapshot $f $current
    Check ((Get-InstallHash $f.Manifest) -eq $journalHash) 'Running-game refusal modified recovery journal.'
}

Test-Case 'exclusive-install-lock-prevents-concurrent-recovery' {
    $f = New-CrashedFixture 'lock-guard'; $current = Current-JournalTargets $f; $journalHash = Get-InstallHash $f.Manifest
    $lease = Enter-InstallLock $f.Game
    try {
        Expect-Failure { Install-Fixture $f } 'Another install or removal'
        Expect-Failure { Uninstall-Fixture $f } 'Another install or removal'
        Assert-Snapshot $f $current
        Check ((Get-InstallHash $f.Manifest) -eq $journalHash) 'Competing installer changed recovery journal.'
    } finally { $lease.Dispose() }
}

Test-Case 'uninstall-recovers-first-and-preserves-user-configuration' {
    $f = New-CrashedFixture 'recover-uninstall'
    $config = Join-Path $f.Game 'BepInEx/RizToolbox/settings.json'; $configHash = Get-InstallHash $config
    Uninstall-Fixture $f; Uninstall-Fixture $f
    Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'winhttp.dll'))) 'Uninstall left unchanged owned loader.'
    Check ((Get-InstallHash $config) -eq $configHash) 'Uninstall after recovery removed user configuration.'
    $journal = Read-RawManifest $f
    Check ($journal.files.Count -eq 1 -and $journal.pending.Count -eq 0 -and $journal.PSObject.Properties.Name -notcontains 'transaction') 'Uninstall did not retain exactly the modified configuration ownership.'
}

$report = [pscustomobject]@{
    scope='Synthetic repositories and game-directory fixtures only; no real installation or game launch.'
    testRoot=$testRoot; count=$results.Count; passed=@($results | Where-Object passed).Count
    results=$results.ToArray()
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding utf8
if ($EvidenceDirectory) {
    New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'results.json') -Encoding utf8
}
Write-Output "$($report.passed)/$($report.count) install recovery checks passed. Fixtures: $testRoot"
if ($report.passed -ne $report.count) { exit 1 }
