#requires -Version 7.2
[CmdletBinding()]
param([string]$GameAssemblyPath = 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repoRoot ('artifacts\script-safety-' + [Guid]::NewGuid().ToString('N'))
$gameRoot = Join-Path $testRoot 'fake-game'
New-Item -ItemType Directory -Path $gameRoot -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $gameRoot 'Rizline.exe'), [byte[]]@())
Copy-Item -LiteralPath $GameAssemblyPath -Destination (Join-Path $gameRoot 'GameAssembly.dll')
$manifestPath = Join-Path $gameRoot 'BepInEx\RizToolbox\installation.json'
$package = Join-Path $repoRoot 'artifacts\package\BepInEx\plugins\RizToolbox'
$sdkPath = Join-Path $gameRoot 'BepInEx\plugins\RizToolbox\RizToolbox.Sdk.dll'
$settingsPath = Join-Path $gameRoot 'BepInEx\RizToolbox\settings.json'
$passed = [Collections.Generic.List[string]]::new()
function Assert-Test([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Failure([scriptblock]$Action, [string]$Contains) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    Assert-Test ($null -ne $caught -and $caught.Contains($Contains)) "Expected failure containing '$Contains'; got '$caught'."
}
function Pass([string]$Name) { $passed.Add($Name); Write-Output "PASS $Name" }
function Install-Test { & (Join-Path $PSScriptRoot 'Install.ps1') -GameDirectory $gameRoot | Out-Null }
function Uninstall-Test { & (Join-Path $PSScriptRoot 'Uninstall.ps1') -GameDirectory $gameRoot | Out-Null }

try {
    $foreign = Join-Path $gameRoot 'winhttp.dll'
    Copy-Item -LiteralPath (Join-Path $repoRoot '.tools\bepinex-789\winhttp.dll') -Destination $foreign
    Expect-Failure { Install-Test } 'Existing loader installation'
    Assert-Test (-not (Test-Path -LiteralPath $manifestPath)) 'Foreign-loader refusal wrote an ownership manifest.'
    Remove-Item -LiteralPath $foreign
    Pass 'foreign loader refused even when its bytes match'

    Install-Test
    $installed = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test ($installed.files.Count -gt 10 -and @($installed.pending).Count -eq 0) 'Installation manifest was not finalized.'
    foreach ($entry in $installed.files) { Assert-Test ((Get-InstallHash (Join-Path $gameRoot $entry.path)) -eq $entry.sha256) "Installed hash mismatch: $($entry.path)" }
    $kernelRelative = 'BepInEx\plugins\RizToolbox\RizToolbox.EngineNodeKernel.dll'
    $kernelEntries = @($installed.files | Where-Object path -eq $kernelRelative)
    Assert-Test ($kernelEntries.Count -eq 1 -and $kernelEntries[0].sha256 -eq (Get-InstallHash (Join-Path $package 'RizToolbox.EngineNodeKernel.dll'))) 'The exact new Kernel payload was not installed and hash-owned once.'
    Pass 'new Kernel payload is installed once with exact package hash ownership'
    $initialCount = $installed.files.Count
    $productionSettings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Assert-Test (-not $productionSettings.enableExamples -and -not $productionSettings.runSelfValidation) 'Production install enabled probes or self-exit validation by default.'
    Pass 'production install keeps example mutations and self-exit validation disabled'
    $config = Get-Content -LiteralPath (Join-Path $gameRoot 'doorstop_config.ini') -Raw
    Assert-Test ($config.Contains('target_assembly = BepInEx\plugins\RizToolbox\RizToolbox.Loader.dll')) 'Doorstop does not target the direct bridge.'
    Install-Test
    $again = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test ($again.files.Count -eq $initialCount) 'Idempotent install lost ownership entries.'
    Assert-Test (@($again.files | Where-Object path -eq $kernelRelative).Count -eq 1) 'Repeated install duplicated or lost Kernel ownership.'
    Pass 'direct Doorstop install and repeated install preserve all hash ownership'

    # Recreate the previous owned layout with no Kernel; all other files and
    # user settings remain owned. Upgrading must add the new payload once.
    Remove-Item -LiteralPath (Join-Path $gameRoot $kernelRelative)
    $again.files = @($again.files | Where-Object path -ne $kernelRelative)
    Write-InstallManifest $manifestPath $again
    Install-Test
    $upgraded = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test ($upgraded.files.Count -eq $initialCount -and @($upgraded.files | Where-Object path -eq $kernelRelative).Count -eq 1) 'Upgrade from the previous layout did not add Kernel ownership exactly once.'
    Assert-Test ((Get-InstallHash (Join-Path $gameRoot $kernelRelative)) -eq $kernelEntries[0].sha256) 'Upgrade installed different Kernel bytes.'
    Pass 'previous owned layout upgrades by adding the exact Kernel payload'

    $kernelPath = Join-Path $gameRoot $kernelRelative
    $kernelOriginal = [IO.File]::ReadAllBytes($kernelPath)
    [IO.File]::WriteAllBytes($kernelPath, [byte[]]@(7,8,9))
    $editedKernelHash = Get-InstallHash $kernelPath
    Expect-Failure { Install-Test } 'Modified/unowned target'
    Assert-Test ((Get-InstallHash $kernelPath) -eq $editedKernelHash) 'Update overwrote the modified Kernel.'
    [IO.File]::WriteAllBytes($kernelPath, $kernelOriginal)
    Pass 'modified Kernel refuses update without overwriting user bytes'

    $settingsOriginal = [IO.File]::ReadAllBytes($settingsPath)
    '{"enableExamples":false,"runSelfValidation":false,"custom":true}' | Set-Content -LiteralPath $settingsPath -Encoding utf8
    $settingsEditedHash = Get-InstallHash $settingsPath
    Install-Test
    Assert-Test ((Get-InstallHash $settingsPath) -eq $settingsEditedHash) 'Update overwrote user settings.'
    Pass 'user-edited settings survive update'

    $sdkOriginal = [IO.File]::ReadAllBytes($sdkPath)
    $sdkOriginalHash = Get-InstallHash $sdkPath
    [IO.File]::WriteAllBytes($sdkPath, [byte[]]@(1,2,3))
    $modifiedHash = Get-InstallHash $sdkPath
    Expect-Failure { Install-Test } 'Modified/unowned target'
    Assert-Test ((Get-InstallHash $sdkPath) -eq $modifiedHash) 'Modified file was replaced.'
    [IO.File]::WriteAllBytes($sdkPath, $sdkOriginal)
    Pass 'modified owned assembly refuses update without overwrite'

    $journal = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $sdkEntry = $journal.files | Where-Object path -eq 'BepInEx\plugins\RizToolbox\RizToolbox.Sdk.dll'
    $journal.files = @($journal.files | Where-Object path -ne $sdkEntry.path)
    Write-InstallManifest $manifestPath $journal
    Expect-Failure { Install-Test } 'Modified/unowned target'
    Assert-Test ((Get-InstallHash $sdkPath) -eq $sdkOriginalHash) 'Equal-content unowned file was changed.'
    $journal.files = @($journal.files) + @($sdkEntry)
    Write-InstallManifest $manifestPath $journal
    Pass 'equal-content unowned target is never silently adopted'

    [IO.File]::WriteAllBytes($sdkPath, [byte[]]@(4,5,6))
    $journal.pending = @([pscustomobject]@{path=$sdkEntry.path;sha256=(Get-InstallHash $sdkPath);previousSha256=$sdkOriginalHash})
    Write-InstallManifest $manifestPath $journal
    Install-Test
    Assert-Test ((Get-InstallHash $sdkPath) -eq $sdkOriginalHash) 'Interrupted update did not recover ownership and restore package.'
    $journal = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test (@($journal.pending).Count -eq 0) 'Recovered journal was not finalized.'
    Pass 'interrupted-copy journal recovers new bytes before next update'

    $journal.PSObject.Properties.Remove('pending')
    Write-InstallManifest $manifestPath $journal
    Install-Test
    Pass 'legacy installation manifest migrates without losing files'

    $legacyChangelog = Join-Path $gameRoot 'changelog.txt'
    'legacy diagnostic loader changelog' | Set-Content -LiteralPath $legacyChangelog -Encoding utf8
    $journal = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $journal.files += [pscustomobject]@{path='changelog.txt';sha256=(Get-InstallHash $legacyChangelog)}
    Write-InstallManifest $manifestPath $journal
    $legacyManifest = Read-InstallManifest $gameRoot $manifestPath
    Assert-Test (@($legacyManifest.files | Where-Object path -eq 'changelog.txt').Count -eq 1) 'Legacy owned root changelog was rejected.'
    Pass 'legacy diagnostic root changelog remains hash-owned'

    $journal = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $validFiles = @($journal.files)
    $journal.files += [pscustomobject]@{path='..\escape.txt';sha256=('0' * 64)}
    Write-InstallManifest $manifestPath $journal
    Expect-Failure { Uninstall-Test } 'Unsafe manifest path'
    Assert-Test (Test-Path -LiteralPath $sdkPath) 'Malformed manifest partially uninstalled valid entries.'
    $journal.files = $validFiles
    Write-InstallManifest $manifestPath $journal
    Pass 'unsafe manifest is rejected before uninstall removes anything'

    $journal.files += [pscustomobject]@{path='GameAssembly.dll';sha256=(Get-InstallHash (Join-Path $gameRoot 'GameAssembly.dll'))}
    Write-InstallManifest $manifestPath $journal
    Expect-Failure { Uninstall-Test } 'outside loader-owned locations'
    Assert-Test (Test-Path -LiteralPath (Join-Path $gameRoot 'GameAssembly.dll')) 'A crafted manifest removed the original game assembly.'
    $journal.files = $validFiles
    Write-InstallManifest $manifestPath $journal
    Pass 'manifest cannot claim original game binaries'

    $generatedPath = Join-Path $gameRoot 'BepInEx\RizToolbox\user-data.txt'
    'user generated data' | Set-Content -LiteralPath $generatedPath -Encoding utf8
    [IO.File]::WriteAllBytes($sdkPath, [byte[]]@(1,2,3))
    Uninstall-Test
    Assert-Test ((Get-InstallHash $sdkPath) -eq $modifiedHash -and (Get-InstallHash $settingsPath) -eq $settingsEditedHash) 'Uninstall removed modified files.'
    Assert-Test (Test-Path -LiteralPath $generatedPath) 'Uninstall removed generated data.'
    $remaining = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test (@($remaining.files).Count -eq 2) 'Uninstall did not retain ownership only for modified files.'
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $gameRoot $kernelRelative))) 'Uninstall retained unchanged owned Kernel payload.'
    [IO.File]::WriteAllBytes($sdkPath, $sdkOriginal)
    [IO.File]::WriteAllBytes($settingsPath, $settingsOriginal)
    Uninstall-Test
    Uninstall-Test
    $remaining = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-Test (@($remaining.files).Count -eq 0 -and -not (Test-Path -LiteralPath $sdkPath)) 'Idempotent uninstall did not finish.'
    Pass 'uninstall preserves modified/generated files and supports repeat removal'

    Install-Test
    Assert-Test (Test-Path -LiteralPath $sdkPath) 'Reinstall after uninstall failed.'
    Uninstall-Test
    Pass 'reinstall succeeds alongside retained generated data'

    $report = [pscustomobject]@{passed=$passed.ToArray();count=$passed.Count;gameWasLaunched=$false;testRoot=$testRoot}
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding utf8
    Write-Output "$($passed.Count) script safety checks passed. Results: $testRoot\results.json"
} catch {
    Write-Error "Script safety check failed; artifacts retained at $testRoot. $_"
    throw
}
