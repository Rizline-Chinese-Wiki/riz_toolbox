#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$EvidenceDirectory,
    [string]$Case = '*',
    [switch]$KeepFixtures
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Test-PreviewDistribution.Inputs.ps1')
if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
$repository = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path)
$requiredScripts = @('PackageDistribution.Common.ps1','Install.Common.ps1','Install.ps1','Uninstall.ps1','Collect-DistributionEvidence.ps1')
$sourceIdentities = foreach ($name in $requiredScripts) {
    $path = Join-Path $repository "scripts/$name"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing test subject: $path" }
    [pscustomobject]@{ path = "scripts/$name"; sha256 = (Get-PreviewHash $path) }
}
$testIdentities = @(
    [pscustomobject]@{ path = $PSCommandPath; sha256 = (Get-PreviewHash $PSCommandPath) }
    [pscustomobject]@{ path = (Join-Path $PSScriptRoot 'Test-PreviewDistribution.Inputs.ps1'); sha256 = (Get-PreviewHash (Join-Path $PSScriptRoot 'Test-PreviewDistribution.Inputs.ps1')) }
)
. (Join-Path $repository 'scripts/PackageDistribution.Common.ps1')
. (Join-Path $repository 'scripts/Install.Common.ps1')
if (Get-Variable -Name RizPreviewDistributionTestState -Scope Global -ErrorAction Ignore) {
    throw 'A preview distribution test invocation is already active in this PowerShell session.'
}
$runId = [Guid]::NewGuid().ToString('N')
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repository "artifacts/preview-distribution-$runId" }
$evidenceRoot = [IO.Path]::TrimEndingDirectorySeparator($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceDirectory))
$fixtureRoot = Join-Path $evidenceRoot 'fixtures'
$resultPath = Join-Path $evidenceRoot 'results.json'
$null = Assert-PreviewPathInside $evidenceRoot $fixtureRoot
if ((Test-Path -LiteralPath $fixtureRoot) -or (Test-Path -LiteralPath $resultPath)) {
    throw 'Choose a fresh EvidenceDirectory; existing fixture trees and reports are never reused.'
}
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$fixtures = [Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow
$global:RizPreviewDistributionTestState = [pscustomobject]@{
    FaultSource = $null; FaultBackupSource = $null; FailRollback = $false; Fixture = $null
    PackageCopyFailed = $false; RollbackCopyFailed = $false; PriorReplacementCount = 0
    JournalId = $null; SelectedBackup = $null; Messages = [Collections.Generic.List[string]]::new()
}

function Check-Preview([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

function Assert-PreviewSet([string[]]$Actual, [string[]]$Expected, [string]$Message) {
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Actual) { Check-Preview ($set.Add($path)) "$Message Duplicate: $path" }
    Check-Preview ($set.Count -eq $Expected.Count) "$Message Expected $($Expected.Count), got $($set.Count)."
    foreach ($path in $Expected) { Check-Preview ($set.Contains($path)) "$Message Missing: $path" }
}

function Assert-PreviewHashes([string]$Root, [System.Collections.IDictionary]$Expected, [string]$Message) {
    foreach ($relative in $Expected.Keys) {
        $path = Assert-PreviewPathInside $Root (Join-Path $Root $relative)
        Check-Preview (Test-Path -LiteralPath $path -PathType Leaf) "$Message Missing file: $relative"
        Check-Preview ((Get-PreviewHash $path) -ceq $Expected[$relative]) "$Message Changed file: $relative"
    }
}

function Assert-PreviewTree($Fixture, $Expected) {
    $actual = Get-PreviewTreeSnapshot $Fixture.Game -IgnoreInstallerManagement
    Assert-PreviewSet @($actual.Keys) @($Expected.Keys) 'Synthetic game payload set changed unexpectedly.'
    Assert-PreviewHashes $Fixture.Game $Expected 'Synthetic game payload changed unexpectedly.'
    Assert-PreviewHashes $Fixture.Game $Fixture.OriginalGameHashes 'Original synthetic game bytes changed.'
    Check-Preview ((Get-PreviewHash (Join-Path $Fixture.Root 'outside-game/canary.txt')) -ceq $Fixture.OutsideCanaryHash) 'Outside-game canary changed.'
}

function Read-PreviewInstallation($Fixture) {
    Get-Content -LiteralPath $Fixture.InstallationManifest -Raw -ErrorAction Stop | ConvertFrom-Json
}

function Assert-PreviewFinalized($Fixture) {
    $manifest = Read-PreviewInstallation $Fixture
    Check-Preview ($manifest.owner -ceq 'riz_toolbox' -and $manifest.gameRoot -ceq $Fixture.Game) 'Installation ownership identity changed.'
    Check-Preview (@($manifest.pending).Count -eq 0 -and $manifest.PSObject.Properties.Name -notcontains 'transaction') 'Installation left a live transaction.'
    return $manifest
}

function Assert-PreviewOwnership($Fixture, $ExpectedManifest) {
    $actual = Read-PreviewInstallation $Fixture
    $expectedRows = @($ExpectedManifest.files | ForEach-Object { "$($_.path)|$($_.sha256)" })
    $actualRows = @($actual.files | ForEach-Object { "$($_.path)|$($_.sha256)" })
    Assert-PreviewSet $actualRows $expectedRows 'Rollback changed previous ownership.'
}

function Reset-PreviewFaults {
    $state = $global:RizPreviewDistributionTestState
    $state.FaultSource = $null; $state.FaultBackupSource = $null; $state.FailRollback = $false; $state.Fixture = $null
    $state.PackageCopyFailed = $false; $state.RollbackCopyFailed = $false; $state.PriorReplacementCount = 0
    $state.JournalId = $null; $state.SelectedBackup = $null; $state.Messages.Clear()
}

function Disable-PreviewCopyFaults {
    $state = $global:RizPreviewDistributionTestState
    $state.FaultSource = $null; $state.FaultBackupSource = $null; $state.FailRollback = $false
}

function Copy-Item {
    [CmdletBinding()]
    param([string]$LiteralPath, [string]$Destination, [switch]$Recurse, [switch]$Force)
    $state = $global:RizPreviewDistributionTestState
    if ($state.FaultBackupSource -and $LiteralPath -ceq $state.FaultBackupSource) {
        $state.RollbackCopyFailed = $true
        throw 'Injected preview rollback copy failure.'
    }
    if ($state.FaultSource -and $LiteralPath -ceq $state.FaultSource) {
        $state.PackageCopyFailed = $true
        $fixture = $state.Fixture
        $journal = Read-PreviewInstallation $fixture
        Check-Preview ($journal.PSObject.Properties.Name -contains 'transaction') 'Copy injection occurred before the real transaction journal.'
        $state.JournalId = $journal.transaction.id
        $replaced = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt @($journal.pending).Count; ++$index) {
            $entry = $journal.pending[$index]
            $target = Get-InstallTarget $fixture.Game $entry.path
            if ($entry.previousSha256 -and (Test-Path -LiteralPath $target -PathType Leaf) -and
                (Get-PreviewHash $target) -ceq $entry.sha256 -and $entry.sha256 -cne $entry.previousSha256) {
                $replaced.Add([pscustomobject]@{ index = $index; entry = $entry })
            }
        }
        $state.PriorReplacementCount = $replaced.Count
        if ($state.FailRollback) {
            Check-Preview ($replaced.Count -gt 0) 'Rollback failure requires an actually replaced target.'
            # Recovery iterates in reverse. Fail the last actual replacement's
            # backup copy, after the normal preflight validated all backups.
            $selected = $replaced[$replaced.Count - 1]
            $state.FaultBackupSource = Get-InstallTarget $fixture.Game (Get-InstallBackupRelative $journal.transaction.id $selected.index)
            Check-Preview ((Get-PreviewHash $state.FaultBackupSource) -ceq $selected.entry.previousSha256) 'Injected rollback backup is not the old payload.'
            $state.SelectedBackup = [pscustomobject]@{
                path = $state.FaultBackupSource; index = $selected.index; target = $selected.entry.path
                previousSha256 = $selected.entry.previousSha256; intendedSha256 = $selected.entry.sha256
            }
        }
        Write-PreviewJson $fixture.Root (Join-Path $fixture.Root 'faults/copy-interruption.json') @{
            transactionId = $state.JournalId; source = $LiteralPath; replacedTargets = $replaced.Count
            rollbackFault = [bool]$state.FailRollback; selectedBackup = $state.SelectedBackup
        }
        throw 'Injected preview package copy failure.'
    }
    Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters
}

function Get-Process {
    [CmdletBinding()]
    param([string]$Name)
    # Only synthetic directories are installed. Do not inspect any real game.
    if ($Name -cne 'Rizline') { throw "Unexpected fixture process query: $Name" }
}

function Publish-PreviewInputs($Fixture) {
    $distribution = New-RuntimeDistribution -RepoRoot $Fixture.RepoRoot -PackageKind $Fixture.PackageKind `
        -NativeOutputDirectory $Fixture.NativeOutputDirectory -DoorstopOutput $Fixture.DoorstopOutput `
        -EngineKernelOutput $Fixture.EngineKernelOutput -PlaybackRuntimeOutput $Fixture.PlaybackRuntimeOutput `
        -OutputDirectory $Fixture.ArtifactRoot -SdkDirectory $Fixture.SdkDirectory -BasicPluginIds $Fixture.BasicPluginIds
    $Fixture.Distribution = $distribution
}

function Publish-PreviewRevision($Fixture, [string]$Revision) {
    Set-PreviewDistributionInputs $Fixture $Revision
    Publish-PreviewInputs $Fixture
}

function New-PreviewCaseFixture([string]$Name, [string]$Kind, [string[]]$BasicPluginIds = @()) {
    $fixture = New-PreviewFixture -FixtureRoot $fixtureRoot -RepositoryRoot $repository -Name $Name -PackageKind $Kind -BasicPluginIds $BasicPluginIds
    $fixtures.Add($fixture)
    Publish-PreviewRevision $fixture 'v1'
    return $fixture
}

function Invoke-PreviewInstall {
    param($Fixture, [hashtable]$Options = @{}, [string]$RequestedKind)
    if (-not $RequestedKind) { $RequestedKind = $Fixture.PackageKind }
    foreach ($key in @('GameDirectory','DistributionDirectory','PackageKind','CandidateDirectory')) {
        Check-Preview (-not $Options.ContainsKey($key)) "Fixture options cannot redirect $key."
    }
    $state = $global:RizPreviewDistributionTestState
    $state.Fixture = $Fixture; $state.Messages.Clear()
    $location = Split-Path -Parent $Fixture.Distribution.ManifestPath
    & (Join-Path $Fixture.RepoRoot 'scripts/Install.ps1') -GameDirectory $Fixture.Game `
        -PackageKind $RequestedKind -DistributionDirectory $location @Options 6>&1 |
        ForEach-Object { $state.Messages.Add([string]$_) }
}

function Invoke-PreviewUninstall($Fixture) {
    & (Join-Path $Fixture.RepoRoot 'scripts/Uninstall.ps1') -GameDirectory $Fixture.Game | Out-Null
}

function Expect-PreviewFailure([scriptblock]$Action, [string[]]$Contains) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
    Check-Preview ($null -ne $failure) 'An operation expected to fail returned successfully.'
    foreach ($fragment in $Contains) {
        Check-Preview ($failure.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) "Expected '$fragment'; got '$failure'."
    }
}

function Assert-PreviewNoInstallSuccess {
    $success = @($global:RizPreviewDistributionTestState.Messages | Where-Object { $_.StartsWith('Installed ', [StringComparison]::Ordinal) })
    Check-Preview ($success.Count -eq 0) 'Failed installation reported success.'
}

function Assert-PreviewLayout($Fixture) {
    $distribution = $Fixture.Distribution
    $manifest = Get-Content -LiteralPath $distribution.ManifestPath -Raw | ConvertFrom-Json
    Assert-RuntimeDistribution $distribution.PackageRoot $manifest $Fixture.PackageKind
    Check-Preview ($manifest.schemaVersion -eq 3 -and $manifest.packageKind -ceq $Fixture.PackageKind -and
        $manifest.acceptance -ceq 'preview/unverified' -and $manifest.version -ceq '0.3.0') 'Generated distribution identity is not schema 3 preview.'
    Check-Preview ($manifest.developmentAssetsInstalled -is [bool] -and -not $manifest.developmentAssetsInstalled -and
        $manifest.proofEnabledByPackage -is [bool] -and -not $manifest.proofEnabledByPackage) 'A package enables development assets or proof.'
    Check-Preview ($manifest.PSObject.Properties.Name -contains 'basicPlugins' -and $manifest.basicPlugins -is [array]) 'Schema 3 package lacks its explicit basicPlugins array.'
    Assert-PreviewSet @($manifest.basicPlugins) @($Fixture.BasicPluginIds) 'Basic plugin selection changed.'
    $runtimePaths = @(Get-RuntimeDistributionPaths -BasicPluginIds @($manifest.basicPlugins))
    Assert-PreviewSet $runtimePaths @(Get-PreviewExpectedRuntimePaths $Fixture.BasicPluginIds) 'Runtime path helper changed the supported file boundary.'
    $resourcePaths = @(Get-PreviewExpectedResources $Fixture.PackageKind)
    Assert-PreviewSet @($manifest.files.path) $runtimePaths 'Runtime payload boundary changed.'
    $actualResources = @($manifest.resources | ForEach-Object { $_.path })
    Assert-PreviewSet $actualResources $resourcePaths 'Development resource boundary changed.'
    $actualFiles = Get-PreviewTreeSnapshot $distribution.PackageRoot
    Assert-PreviewSet @($actualFiles.Keys) @($runtimePaths + $resourcePaths) 'Package contains extra or missing bytes.'
    foreach ($entry in @($manifest.files) + @($manifest.resources)) {
        Check-Preview ($entry.sha256 -cmatch '^[0-9a-f]{64}$' -and $entry.sha256 -ceq $actualFiles[$entry.path]) "Generated hash is incorrect: $($entry.path)"
    }
    $inputs = @{
        'winhttp.dll' = (Join-Path $Fixture.DoorstopOutput 'winhttp.dll')
        'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll' = (Join-Path $Fixture.NativeOutputDirectory 'RizToolbox.JudgeContext.dll')
        'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll' = (Join-Path $Fixture.EngineKernelOutput 'RizToolbox.EngineNodeKernel.dll')
        'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json' = (Join-Path $Fixture.RepoRoot 'src/RizToolbox.Adapter/profiles/steam-25977441.json')
    }
    foreach ($name in @('Sdk','Core','Runtime','Adapter','Hosting','Loader')) {
        $inputs["BepInEx/plugins/RizToolbox/RizToolbox.$name.dll"] = Join-Path $Fixture.RepoRoot "src/RizToolbox.$name/bin/Release/net6.0/RizToolbox.$name.dll"
    }
    foreach ($name in @('RizToolbox.SingleReturn.Prototype.dll','RizToolbox.PlaybackSourcePrototype.dll')) {
        $inputs["BepInEx/plugins/RizToolbox/$name"] = Join-Path $Fixture.PlaybackRuntimeOutput $name
    }
    foreach ($name in @('doorstop-corresponding-source.zip','UnityDoorstop.LGPL-2.1.txt')) {
        $inputs["BepInEx/plugins/RizToolbox/licenses/$name"] = Join-Path $Fixture.DoorstopOutput $name
    }
    foreach ($id in $Fixture.BasicPluginIds) {
        $inputs["BepInEx/RizToolbox/mods/$id/plugin.json"] = Join-Path $Fixture.RepoRoot "examples/basic-packages/$id/plugin.json"
        $inputs["BepInEx/RizToolbox/mods/$id/RizToolbox.BasicPlugins.dll"] = Join-Path $Fixture.RepoRoot 'examples/RizToolbox.BasicPlugins/bin/Release/net6.0/RizToolbox.BasicPlugins.dll'
    }
    foreach ($relative in $inputs.Keys) {
        Check-Preview ($actualFiles[$relative] -ceq (Get-PreviewHash $inputs[$relative])) "Packager selected wrong input bytes: $relative"
    }
    Check-Preview ([IO.File]::ReadAllText((Join-Path $distribution.PackageRoot '.doorstop_version')) -ceq '4.5.0-riz-preinit.2') 'Unexpected Doorstop version marker.'
    $expectedPublication = if ($Fixture.PackageKind -eq 'Runtime') { $Fixture.ArtifactRoot } else { Join-Path $Fixture.ArtifactRoot $Fixture.PackageKind.ToLowerInvariant() }
    Check-Preview ((Split-Path -Parent $distribution.PackageRoot) -ceq $expectedPublication) 'Package kind used another publication directory.'
    if ($Fixture.PackageKind -eq 'Runtime') { return }
    $prefix = if ($Fixture.PackageKind -eq 'Author') { 'author/examples' } else { 'validation/proof' }
    foreach ($id in Get-PreviewExampleIds) {
        Check-Preview ($actualFiles["$prefix/packages/$id/plugin.json"] -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot "examples/packages/$id/plugin.json"))) 'Example manifest input changed.'
        Check-Preview ($actualFiles["$prefix/packages/$id/RizToolbox.Examples.dll"] -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot 'examples/RizToolbox.Examples/bin/Release/net6.0/RizToolbox.Examples.dll'))) 'Example assembly input changed.'
    }
    if ($Fixture.PackageKind -eq 'Author') {
        foreach ($name in @('RizToolbox.Sdk.1.4.0.nupkg','sdk-manifest.json')) {
            Check-Preview ($actualFiles["author/sdk/$name"] -ceq (Get-PreviewHash (Join-Path $Fixture.SdkDirectory $name))) 'Author feed bytes changed.'
        }
        $sdk = Get-Content -LiteralPath (Join-Path $distribution.PackageRoot 'author/sdk/sdk-manifest.json') -Raw | ConvertFrom-Json
        Check-Preview ($sdk.sha256 -ceq $actualFiles['author/sdk/RizToolbox.Sdk.1.4.0.nupkg']) 'Author feed manifest lost its package hash.'
        $project = [xml](Get-Content -LiteralPath (Join-Path $distribution.PackageRoot 'author/examples/source/RizToolbox.Examples.csproj') -Raw)
        Check-Preview ($null -eq $project.SelectSingleNode('/Project/ItemGroup/ProjectReference')) 'Author source retains a repository ProjectReference.'
        $reference = $project.SelectSingleNode('/Project/ItemGroup/PackageReference')
        Check-Preview ($null -ne $reference -and $reference.Include -ceq 'RizToolbox.Sdk' -and $reference.Version -ceq '1.4.0' -and
            $reference.IncludeAssets -ceq 'compile' -and $reference.PrivateAssets -ceq 'all') 'Author source does not use the compile-only released SDK feed.'
        foreach ($name in @('ColorPlugin.cs','PanelPlugin.cs')) {
            Check-Preview ($actualFiles["author/examples/source/$name"] -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot "examples/RizToolbox.Examples/$name"))) 'Author source bytes changed.'
        }
        foreach ($relative in @('.template.config/template.json','Plugin.cs','README.md','RizModProject.csproj','plugin.json')) {
            Check-Preview ($actualFiles["author/templates/riz-plugin/$relative"] -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot "templates/riz-plugin/$relative"))) 'Author template bytes changed.'
        }
    } else {
        Check-Preview ($actualFiles['validation/fixtures/JudgeContext.Fixture.exe'] -ceq (Get-PreviewHash (Join-Path $Fixture.NativeOutputDirectory 'JudgeContext.Fixture.exe'))) 'Validation JudgeContext fixture changed.'
        Check-Preview ($actualFiles['validation/fixtures/EngineNodeKernel.EntryFixture.exe'] -ceq (Get-PreviewHash (Join-Path $Fixture.EngineKernelOutput 'EngineNodeKernel.EntryFixture.exe'))) 'Validation kernel fixture changed.'
        foreach ($name in @('Collect-DistributionEvidence.ps1','PackageDistribution.Common.ps1')) {
            Check-Preview ($actualFiles["validation/scripts/$name"] -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot "scripts/$name"))) 'Validation evidence script bytes changed.'
        }
        $proof = Get-Content -LiteralPath (Join-Path $distribution.PackageRoot 'validation/proof/runtime.developer.example.json') -Raw | ConvertFrom-Json
        Check-Preview ($proof.mode -ceq 'Developer' -and $proof.enableProofPlugins -and $proof.fullAuditEachTick) 'Validation lost its explicitly selectable proof example.'
    }
}

function Assert-PreviewNoDevelopmentInstall($Fixture, [hashtable]$UserHashes = @{}) {
    foreach ($relative in @('author','validation','BepInEx/plugins/RizToolbox/RizToolbox.Examples.dll') + @(Get-PreviewExpectedResources $Fixture.PackageKind)) {
        Check-Preview (-not (Test-Path -LiteralPath (Join-Path $Fixture.Game $relative))) "Development resource was installed: $relative"
    }
    foreach ($id in Get-PreviewExampleIds) {
        foreach ($name in @('plugin.json','RizToolbox.Examples.dll')) {
            $relative = "BepInEx/RizToolbox/mods/$id/$name"
            if ($UserHashes.ContainsKey($relative)) { continue }
            Check-Preview (-not (Test-Path -LiteralPath (Join-Path $Fixture.Game $relative))) "Unselected example was installed: $relative"
        }
    }
    foreach ($id in Get-PreviewBasicPluginIds) {
        if ($id -cin $Fixture.BasicPluginIds) { continue }
        foreach ($name in @('plugin.json','RizToolbox.BasicPlugins.dll')) {
            $relative = "BepInEx/RizToolbox/mods/$id/$name"
            if ($UserHashes.ContainsKey($relative)) { continue }
            Check-Preview (-not (Test-Path -LiteralPath (Join-Path $Fixture.Game $relative))) "Unselected basic plugin was installed: $relative"
        }
    }
    $manifest = Read-PreviewInstallation $Fixture
    foreach ($entry in $manifest.files) {
        $relative = $entry.path.Replace('\','/')
        if ($relative.StartsWith('BepInEx/RizToolbox/mods/', [StringComparison]::Ordinal) -and
            $relative -cin @(Get-PreviewExpectedRuntimePaths $Fixture.BasicPluginIds)) { continue }
        Check-Preview ($relative -notmatch '^(author/|validation/|BepInEx/RizToolbox/(mods|plugins|logs|diagnostics)/)' -and
            $relative -cne 'BepInEx/RizToolbox/activation.json') "Installer claimed user/development data: $relative"
    }
}

function Assert-PreviewInstalledPayload($Fixture) {
    $manifest = Assert-PreviewFinalized $Fixture
    $expected = @(Get-RuntimeDistributionPaths -BasicPluginIds @($Fixture.Distribution.Manifest.basicPlugins)) + @(
        'BepInEx/core/dobby.dll','dotnet/runtime.dll','dotnet/host/hostfxr.dll','doorstop_config.ini',
        'BepInEx/RizToolbox/settings.json','BepInEx/RizToolbox/runtime.json'
    )
    Assert-PreviewSet @($manifest.files | ForEach-Object { $_.path.Replace('\','/') }) $expected 'Installed file ownership changed.'
    Check-Preview (@($manifest.files).Count -eq $expected.Count) 'Installed ownership count changed.'
    foreach ($entry in $Fixture.Distribution.Manifest.files) {
        Check-Preview ((Get-PreviewHash (Join-Path $Fixture.Game $entry.path)) -ceq $entry.sha256) "Installed runtime differs from generated package: $($entry.path)"
    }
    foreach ($relative in @('BepInEx/core/dobby.dll','dotnet/runtime.dll','dotnet/host/hostfxr.dll')) {
        Check-Preview ((Get-PreviewHash (Join-Path $Fixture.Game $relative)) -ceq (Get-PreviewHash (Join-Path $Fixture.RepoRoot ".tools/bepinex-789/$relative"))) 'Installer selected another dependency cache.'
    }
    $doorstop = Get-Content -LiteralPath (Join-Path $Fixture.Game 'doorstop_config.ini') -Raw
    Check-Preview ($doorstop.Contains('target_assembly = BepInEx\plugins\RizToolbox\RizToolbox.Loader.dll')) 'Installed Doorstop does not target the direct Loader.'
    Assert-PreviewHashes $Fixture.Game $Fixture.OriginalGameHashes 'Installation changed original synthetic game bytes.'
}

function Assert-PreviewDefaults($Fixture) {
    $settings = Get-Content -LiteralPath (Join-Path $Fixture.Game 'BepInEx/RizToolbox/settings.json') -Raw | ConvertFrom-Json
    $runtime = Get-Content -LiteralPath (Join-Path $Fixture.Game 'BepInEx/RizToolbox/runtime.json') -Raw | ConvertFrom-Json
    Check-Preview (-not $settings.enableExamples -and -not $settings.runSelfValidation) 'Package defaults enable examples or self-validation.'
    Check-Preview ($runtime.schemaVersion -eq 1 -and $runtime.mode -ceq 'Standard' -and $runtime.logLevel -ceq 'Info' -and
        -not $runtime.fullAuditEachTick -and -not $runtime.enableProofPlugins) 'Package defaults enable Developer/proof behavior.'
}

function Enable-PreviewUpgradeFault($Fixture, [switch]$FailRollback) {
    $state = $global:RizPreviewDistributionTestState
    $state.Fixture = $Fixture
    $state.FaultSource = Join-Path $Fixture.Distribution.PackageRoot 'BepInEx/plugins/RizToolbox/RizToolbox.Loader.dll'
    $state.FailRollback = [bool]$FailRollback
}

function Assert-PreviewJournalBackups($Fixture, $Journal) {
    Check-Preview ($Journal.PSObject.Properties.Name -contains 'transaction' -and @($Journal.pending).Count -gt 0) 'Incomplete recovery cleared its real transaction journal.'
    for ($index = 0; $index -lt @($Journal.pending).Count; ++$index) {
        $entry = $Journal.pending[$index]
        if ($entry.previousSha256) {
            $backup = Get-InstallTarget $Fixture.Game (Get-InstallBackupRelative $Journal.transaction.id $index)
            Check-Preview ((Get-PreviewHash $backup) -ceq $entry.previousSha256) "Known-good rollback backup changed: $($entry.path)"
        }
    }
}

function Invoke-PreviewCase([string]$Name, [scriptblock]$Action) {
    if ($Name -notlike $Case) { return }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action | Out-Null
        $results.Add([pscustomobject]@{ name = $Name; passed = $true; durationMs = $timer.ElapsedMilliseconds })
        Write-Output "PASS $Name"
    } catch {
        $results.Add([pscustomobject]@{
            name = $Name; passed = $false; durationMs = $timer.ElapsedMilliseconds
            error = $_.Exception.Message; stack = $_.ScriptStackTrace
        })
        Write-Output "FAIL $Name : $($_.Exception.Message)"
    } finally { $timer.Stop(); Reset-PreviewFaults }
}

try {
    foreach ($kind in @('Runtime','Author','Validation')) {
        $prefix = $kind.ToLowerInvariant()
        Invoke-PreviewCase "$prefix-generated-schema3-boundaries" {
            $f = New-PreviewCaseFixture "$prefix-generated" $kind
            Assert-PreviewLayout $f
        }

        Invoke-PreviewCase "$prefix-install-upgrade-rollback-preserves-user-files" {
            $f = New-PreviewCaseFixture "$prefix-lifecycle" $kind
            Assert-PreviewLayout $f
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewDefaults $f; Assert-PreviewNoDevelopmentInstall $f
            $previousOwnership = Read-PreviewInstallation $f
            $userHashes = Set-PreviewUserFiles $f
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $oldManifestHash = Get-PreviewHash $f.Distribution.ManifestPath
            $oldLoaderHash = Get-PreviewHash (Join-Path $f.Game 'winhttp.dll')
            Publish-PreviewRevision $f 'v2'; Assert-PreviewLayout $f
            $history = @(Get-ChildItem -LiteralPath (Join-Path $f.ArtifactRoot 'package-history') -Directory -Filter "$prefix-*")
            Check-Preview ($history.Count -eq 1) 'Republishing did not preserve one prior distribution.'
            $null = Assert-PreviewPathInside $f.Root $history[0].FullName
            $historyManifest = Join-Path $history[0].FullName 'package-manifest.json'
            Check-Preview ((Get-PreviewHash $historyManifest) -ceq $oldManifestHash) 'Publication history changed the previous manifest.'
            Assert-RuntimeDistribution (Join-Path $history[0].FullName 'package') (Get-Content -LiteralPath $historyManifest -Raw | ConvertFrom-Json) $kind
            Enable-PreviewUpgradeFault $f
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Injected preview package copy failure')
            Assert-PreviewNoInstallSuccess
            Check-Preview ($global:RizPreviewDistributionTestState.PackageCopyFailed -and $global:RizPreviewDistributionTestState.PriorReplacementCount -gt 0) 'Failed upgrade did not exercise real replacements.'
            Disable-PreviewCopyFaults
            $null = Assert-PreviewFinalized $f; Assert-PreviewOwnership $f $previousOwnership; Assert-PreviewTree $f $before
            Assert-PreviewHashes $f.Game $userHashes 'Automatic rollback changed user files.'
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewHashes $f.Game $userHashes 'Upgrade changed user files.'
            Check-Preview ((Get-PreviewHash (Join-Path $f.Game 'winhttp.dll')) -cne $oldLoaderHash) 'Upgrade did not replace the old runtime bytes.'
            Invoke-PreviewInstall $f @{ RuntimeMode = 'Developer'; LogLevel = 'Debug'; FullAuditEachTick = $true; EnableProofPlugins = $true }
            Assert-PreviewInstalledPayload $f; Assert-PreviewHashes $f.Game $userHashes 'Repeated install or mode options changed existing user files.'
            Assert-PreviewNoDevelopmentInstall $f $userHashes
            Invoke-PreviewUninstall $f; Invoke-PreviewUninstall $f
            Assert-PreviewHashes $f.Game $userHashes 'Uninstall removed user files.'
            $retained = Assert-PreviewFinalized $f
            Assert-PreviewSet @($retained.files | ForEach-Object { $_.path.Replace('\','/') }) @('BepInEx/RizToolbox/settings.json','BepInEx/RizToolbox/runtime.json') 'Uninstall retained another owned payload.'
            foreach ($relative in Get-RuntimeDistributionPaths -BasicPluginIds $f.BasicPluginIds) { Check-Preview (-not (Test-Path -LiteralPath (Join-Path $f.Game $relative))) 'Uninstall left an unchanged runtime payload.' }
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewHashes $f.Game $userHashes 'Reinstallation changed retained user files.'
            Assert-PreviewNoDevelopmentInstall $f $userHashes
        }

        Invoke-PreviewCase "$prefix-failed-first-install-rolls-back-owned-writes" {
            $f = New-PreviewCaseFixture "$prefix-first-failure" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Enable-PreviewUpgradeFault $f
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Injected preview package copy failure')
            Assert-PreviewNoInstallSuccess
            Check-Preview $global:RizPreviewDistributionTestState.PackageCopyFailed 'First-install copy fault was not reached.'
            $manifest = Assert-PreviewFinalized $f
            Check-Preview (@($manifest.files).Count -eq 0) 'Failed first install claimed a successful owned payload.'
            Assert-PreviewTree $f $before
        }

        Invoke-PreviewCase "$prefix-incomplete-recovery-refuses-and-retries" {
            $f = New-PreviewCaseFixture "$prefix-recovery" $kind
            Invoke-PreviewInstall $f
            $previousOwnership = Read-PreviewInstallation $f
            $userHashes = Set-PreviewUserFiles $f
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Publish-PreviewRevision $f 'v2'
            Enable-PreviewUpgradeFault $f -FailRollback
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Installation failed:', 'Injected preview package copy failure', 'Recovery is incomplete:', 'Injected preview rollback copy failure')
            Assert-PreviewNoInstallSuccess
            $state = $global:RizPreviewDistributionTestState
            Check-Preview ($state.PackageCopyFailed -and $state.RollbackCopyFailed -and $state.PriorReplacementCount -gt 0) 'Incomplete recovery did not exercise both real copy failures.'
            $selected = $state.SelectedBackup
            Disable-PreviewCopyFaults
            $journal = Read-PreviewInstallation $f
            Assert-PreviewJournalBackups $f $journal; Assert-PreviewOwnership $f $previousOwnership
            Assert-PreviewHashes $f.Game $userHashes 'Incomplete recovery changed user files.'
            $journalHash = Get-PreviewHash $f.InstallationManifest
            $target = Get-InstallTarget $f.Game $selected.target
            $null = Assert-PreviewPathInside $f.Root $target
            $intendedBytes = [IO.File]::ReadAllBytes($target)
            Write-PreviewInputFile $f.Root $target 'User-modified target after interrupted upgrade.'
            $modified = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('modified')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $modified
            Check-Preview ((Get-PreviewHash $f.InstallationManifest) -ceq $journalHash) 'Modified-target refusal rewrote the recovery journal.'
            Assert-PreviewJournalBackups $f (Read-PreviewInstallation $f)
            [IO.File]::WriteAllBytes($target, $intendedBytes)
            $null = Assert-PreviewPathInside $f.Root $selected.path
            $oldBytes = [IO.File]::ReadAllBytes($selected.path)
            Write-PreviewInputFile $f.Root $selected.path 'Corrupted synthetic backup.'
            $mixed = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('backup hash mismatch')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $mixed
            Check-Preview ((Get-PreviewHash $f.InstallationManifest) -ceq $journalHash) 'Corrupt-backup refusal rewrote the recovery journal.'
            [IO.File]::WriteAllBytes($selected.path, $oldBytes)
            Assert-PreviewJournalBackups $f (Read-PreviewInstallation $f)
            Remove-PreviewFixturePath $f.Root $f.Distribution.ManifestPath
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Run scripts/Build.ps1 first')
            Assert-PreviewNoInstallSuccess
            $null = Assert-PreviewFinalized $f; Assert-PreviewOwnership $f $previousOwnership; Assert-PreviewTree $f $before
            Assert-PreviewHashes $f.Game $userHashes 'Recovery with a missing new package changed user files.'
            Publish-PreviewRevision $f 'v3'
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewHashes $f.Game $userHashes 'Upgrade after completed recovery changed user files.'
            Assert-PreviewNoDevelopmentInstall $f $userHashes
        }

        Invoke-PreviewCase "$prefix-runtime-hash-tamper-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-hash" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot 'winhttp.dll') 'Tampered published runtime bytes.'
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest $kind } @('Package hash mismatch')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Package hash mismatch')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
            Check-Preview (-not (Test-Path -LiteralPath $f.InstallationManifest)) 'Rejected hash wrote an installation manifest.'
        }

        Invoke-PreviewCase "$prefix-cross-kind-install-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-cross-kind" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $otherKind = if ($kind -eq 'Runtime') { 'Author' } else { 'Runtime' }
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $f.Distribution.Manifest $otherKind } @('Invalid package identity')
            Expect-PreviewFailure { Invoke-PreviewInstall -Fixture $f -RequestedKind $otherKind } @('Invalid package identity')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }

        Invoke-PreviewCase "$prefix-unlisted-file-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-unlisted" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot 'unexpected.txt') 'Unlisted package payload.'
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $f.Distribution.Manifest $kind } @('Unlisted package file')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Unlisted package file')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }

        Invoke-PreviewCase "$prefix-missing-input-keeps-previous-publication" {
            $f = New-PreviewCaseFixture "$prefix-missing-input" $kind
            $before = Get-PreviewTreeSnapshot $f.Distribution.PackageRoot
            $previousManifest = Get-PreviewHash $f.Distribution.ManifestPath
            Set-PreviewDistributionInputs $f 'v2'
            $missing = switch ($kind) {
                'Runtime' { Join-Path $f.NativeOutputDirectory 'RizToolbox.JudgeContext.dll' }
                'Author' { Join-Path $f.SdkDirectory 'RizToolbox.Sdk.1.4.0.nupkg' }
                'Validation' { Join-Path $f.EngineKernelOutput 'EngineNodeKernel.EntryFixture.exe' }
            }
            Remove-PreviewFixturePath $f.Root $missing
            Expect-PreviewFailure { Publish-PreviewInputs $f } @('Missing distribution input')
            $actual = Get-PreviewTreeSnapshot $f.Distribution.PackageRoot
            Assert-PreviewSet @($actual.Keys) @($before.Keys) 'Failed generation changed the previous published payload set.'
            Assert-PreviewHashes $f.Distribution.PackageRoot $before 'Failed generation changed the previous published bytes.'
            Check-Preview ((Get-PreviewHash $f.Distribution.ManifestPath) -ceq $previousManifest) 'Failed generation changed the previous manifest.'
            Assert-RuntimeDistribution $f.Distribution.PackageRoot $f.Distribution.Manifest $kind
        }
    }

    foreach ($kind in @('Author','Validation')) {
        $prefix = $kind.ToLowerInvariant()
        Invoke-PreviewCase "$prefix-resource-hash-tamper-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-resource-hash" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $relative = if ($kind -eq 'Author') { 'author/sdk/RizToolbox.Sdk.1.4.0.nupkg' } else { 'validation/fixtures/JudgeContext.Fixture.exe' }
            Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot $relative) 'Tampered development resource bytes.'
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $f.Distribution.Manifest $kind } @('Package hash mismatch')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Package hash mismatch')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }

        Invoke-PreviewCase "$prefix-missing-required-resource-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-missing-resource" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            $missing = if ($kind -eq 'Author') { 'author/sdk/sdk-manifest.json' } else { 'validation/proof/runtime.developer.example.json' }
            $manifest.resources = @($manifest.resources | Where-Object { $_.path -cne $missing })
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest $kind } @('Missing development resource')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Missing development resource')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }
    }

    foreach ($kind in @('Runtime','Author','Validation')) {
        $prefix = $kind.ToLowerInvariant()
        Invoke-PreviewCase "$prefix-resource-location-boundary-is-rejected" {
            $f = New-PreviewCaseFixture "$prefix-resource-boundary" $kind
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            $illegal = if ($kind -eq 'Author') { 'validation/fixtures/JudgeContext.Fixture.exe' } else { 'author/sdk/RizToolbox.Sdk.1.4.0.nupkg' }
            Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot $illegal) 'Resource belonging to another package kind.'
            $manifest.resources = @($manifest.resources) + @([pscustomobject]@{ path = $illegal; sha256 = (Get-PreviewHash (Join-Path $f.Distribution.PackageRoot $illegal)) })
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest $kind } @('Unexpected development resource entry')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Unexpected development resource entry')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }
    }

    foreach ($selection in @(
        @{ Name = 'single'; Ids = @('basic.theme') },
        @{ Name = 'all'; Ids = @(Get-PreviewBasicPluginIds) }
    )) {
        Invoke-PreviewCase "runtime-basic-$($selection.Name)-selection-installs-and-upgrades" {
            $f = New-PreviewCaseFixture "runtime-basic-$($selection.Name)" 'Runtime' $selection.Ids
            Assert-PreviewLayout $f
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewDefaults $f; Assert-PreviewNoDevelopmentInstall $f
            foreach ($id in Get-PreviewBasicPluginIds) {
                foreach ($name in @('plugin.json','RizToolbox.BasicPlugins.dll')) {
                    $path = Join-Path $f.Game "BepInEx/RizToolbox/mods/$id/$name"
                    Check-Preview ((Test-Path -LiteralPath $path -PathType Leaf) -eq ($id -cin $selection.Ids)) 'An unselected basic plugin was installed or a selected one was omitted.'
                }
            }
            $selectedRelative = "BepInEx/RizToolbox/mods/$($selection.Ids[0])/RizToolbox.BasicPlugins.dll"
            $oldHash = Get-PreviewHash (Join-Path $f.Game $selectedRelative)
            $userHashes = Set-PreviewUserFiles $f
            Publish-PreviewRevision $f 'v2'; Assert-PreviewLayout $f
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewNoDevelopmentInstall $f $userHashes
            Assert-PreviewHashes $f.Game $userHashes 'Basic plugin upgrade changed user files.'
            Check-Preview ((Get-PreviewHash (Join-Path $f.Game $selectedRelative)) -cne $oldHash) 'Selected basic plugin did not upgrade its owned bytes.'
            Invoke-PreviewInstall $f
            Assert-PreviewInstalledPayload $f; Assert-PreviewHashes $f.Game $userHashes 'Repeated basic install changed user files.'
        }
    }

    foreach ($badSelection in @(
        @{ Name = 'unknown'; Ids = @('basic.unknown') },
        @{ Name = 'proof'; Ids = @('example.panel') },
        @{ Name = 'duplicate'; Ids = @('basic.theme','basic.theme') },
        @{ Name = 'path'; Ids = @('../basic.theme') }
    )) {
        Invoke-PreviewCase "runtime-basic-$($badSelection.Name)-manifest-is-rejected" {
            $f = New-PreviewCaseFixture "runtime-basic-invalid-$($badSelection.Name)" 'Runtime'
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            $manifest.basicPlugins = @($badSelection.Ids)
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            # Product wording is intentionally not assumed for the new selection
            # validation. Both entry points must reject before writing payloads.
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Runtime' } @()
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @()
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
            Check-Preview (-not (Test-Path -LiteralPath $f.InstallationManifest)) 'Invalid basic selection wrote an installation manifest.'
        }
    }

    foreach ($selectionShape in @('missing','scalar')) {
        Invoke-PreviewCase "runtime-basic-selection-$selectionShape-array-is-rejected" {
            $f = New-PreviewCaseFixture "runtime-basic-shape-$selectionShape" 'Runtime'
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            if ($selectionShape -eq 'missing') { $manifest.PSObject.Properties.Remove('basicPlugins') }
            else { $manifest.basicPlugins = 'basic.theme' }
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Runtime' } @()
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @()
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
            Check-Preview (-not (Test-Path -LiteralPath $f.InstallationManifest)) 'Malformed basic selection array wrote an installation manifest.'
        }
    }

    $badRuntimePaths = @('../outside-game/canary.txt','BepInEx/../outside.txt','GameAssembly.dll','BepInEx/RizToolbox/installation.json','C:/preview-escape.dll')
    for ($badIndex = 0; $badIndex -lt $badRuntimePaths.Count; ++$badIndex) {
        Invoke-PreviewCase "runtime-unsafe-manifest-path-$badIndex-is-rejected" {
            $f = New-PreviewCaseFixture "runtime-unsafe-$badIndex" 'Runtime'
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            $manifest.files[0].path = $badRuntimePaths[$badIndex]
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Runtime' } @('Unexpected runtime package entry')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Unexpected runtime package entry')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }
    }

    Invoke-PreviewCase 'author-source-path-traversal-is-rejected' {
        $f = New-PreviewCaseFixture 'author-source-traversal' 'Author'
        $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
        $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
        $row = @($manifest.resources | Where-Object { $_.path -ceq 'author/examples/source/PanelPlugin.cs' })[0]
        $row.path = 'author/examples/source/../Escape.cs'
        Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
        Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Author' } @('Unexpected development resource entry')
        Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Unexpected development resource entry')
        Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
    }

    Invoke-PreviewCase 'runtime-duplicate-manifest-entry-is-rejected' {
        $f = New-PreviewCaseFixture 'runtime-duplicate' 'Runtime'
        $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
        $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
        $manifest.files = @($manifest.files) + @($manifest.files[0])
        Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
        Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Runtime' } @('Unexpected runtime package entry')
        Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Unexpected runtime package entry')
        Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
    }

    foreach ($policy in @('developmentAssetsInstalled','proofEnabledByPackage','acceptance')) {
        Invoke-PreviewCase "runtime-policy-$policy-is-rejected" {
            $f = New-PreviewCaseFixture ("runtime-policy-" + $policy.ToLowerInvariant()) 'Runtime'
            $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
            $manifest = Get-Content -LiteralPath $f.Distribution.ManifestPath -Raw | ConvertFrom-Json
            if ($policy -eq 'acceptance') { $manifest.acceptance = 'verified' } else { $manifest.$policy = $true }
            Write-PreviewJson $f.Root $f.Distribution.ManifestPath $manifest
            Expect-PreviewFailure { Assert-RuntimeDistribution $f.Distribution.PackageRoot $manifest 'Runtime' } @('Invalid package identity or development policy')
            Expect-PreviewFailure { Invoke-PreviewInstall $f } @('Invalid package identity or development policy')
            Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        }
    }

    Invoke-PreviewCase 'runtime-proof-requires-explicit-developer-configuration' {
        $f = New-PreviewCaseFixture 'runtime-developer-options' 'Runtime'
        $before = Get-PreviewTreeSnapshot $f.Game -IgnoreInstallerManagement
        Expect-PreviewFailure { Invoke-PreviewInstall $f @{ EnableProofPlugins = $true } } @('require explicit Developer mode')
        Assert-PreviewNoInstallSuccess; Assert-PreviewTree $f $before
        Invoke-PreviewInstall $f @{ RuntimeMode = 'Developer'; LogLevel = 'Debug'; FullAuditEachTick = $true; EnableProofPlugins = $true }
        Assert-PreviewInstalledPayload $f; Assert-PreviewNoDevelopmentInstall $f
        $runtime = Get-Content -LiteralPath (Join-Path $f.Game 'BepInEx/RizToolbox/runtime.json') -Raw | ConvertFrom-Json
        Check-Preview ($runtime.mode -ceq 'Developer' -and $runtime.logLevel -ceq 'Debug' -and $runtime.fullAuditEachTick -and $runtime.enableProofPlugins) 'Explicit Developer options were not written to the new runtime configuration.'
        $settings = Get-Content -LiteralPath (Join-Path $f.Game 'BepInEx/RizToolbox/settings.json') -Raw | ConvertFrom-Json
        Check-Preview (-not $settings.enableExamples -and -not $settings.runSelfValidation) 'Explicit proof options silently enabled self-validation.'
    }

    if ($results.Count -eq 0) { throw "No preview distribution cases matched '$Case'." }
    foreach ($identity in $sourceIdentities) {
        Check-Preview ((Get-PreviewHash (Join-Path $repository $identity.path)) -ceq $identity.sha256) "Test subject changed while the suite was running: $($identity.path)"
    }
} catch {
    $results.Add([pscustomobject]@{ name = 'test-infrastructure'; passed = $false; error = $_.Exception.Message; stack = $_.ScriptStackTrace })
    Write-Output "FAIL test-infrastructure : $($_.Exception.Message)"
} finally {
    Remove-Variable -Name RizPreviewDistributionTestState -Scope Global -ErrorAction Ignore
}

$passed = @($results | Where-Object { $_.passed }).Count
$failed = $results.Count - $passed
$cleanup = [Collections.Generic.List[object]]::new()
if ($failed -eq 0 -and -not $KeepFixtures) {
    try {
        Remove-PreviewFixturePath $evidenceRoot $fixtureRoot
        $cleanup.Add([pscustomobject]@{ path = $fixtureRoot; removed = $true })
    } catch {
        $results.Add([pscustomobject]@{ name = 'fixture-cleanup'; passed = $false; error = $_.Exception.Message; stack = $_.ScriptStackTrace })
        $cleanup.Add([pscustomobject]@{ path = $fixtureRoot; removed = $false; error = $_.Exception.Message })
        $failed++
        Write-Output "FAIL fixture-cleanup : $($_.Exception.Message)"
    }
} else {
    $cleanup.Add([pscustomobject]@{ path = $fixtureRoot; removed = $false; reason = $(if ($failed -gt 0) { 'Preserved all fixtures for failure inspection.' } else { 'KeepFixtures requested.' }) })
}
$report = [pscustomobject]@{
    schemaVersion = 1; test = 'PreviewDistribution'; distributionSchemaVersion = 3; runId = $runId
    scope = 'Synthetic repositories, dependency caches, SDK feeds and game directory markers only.'
    repositoryRoot = $repository; startedAt = $startedAt.ToString('o'); finishedAt = [DateTimeOffset]::UtcNow.ToString('o')
    sourceIdentities = @($sourceIdentities); testIdentities = @($testIdentities); fixtureRoot = $fixtureRoot; caseFilter = $Case
    generatedPackageKinds = @($fixtures | ForEach-Object { $_.PackageKind } | Sort-Object -Unique)
    nativeExecuted = $false; gameLaunched = $false; realGameAccess = $false; networkAccess = $false
    count = $results.Count; passed = $passed; failed = $failed; results = $results.ToArray(); cleanup = $cleanup.ToArray()
}
Write-PreviewJson $evidenceRoot $resultPath $report
Write-Output "$($report.passed)/$($report.count) schema 3 preview distribution checks passed. Report: $resultPath"
if ($failed -gt 0) { exit 1 }
