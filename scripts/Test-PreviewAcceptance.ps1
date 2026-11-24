#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$EvidenceDirectory,
    [string]$Case = '*',
    [switch]$KeepFixtures
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
$repository = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path)
$inputHelper = Join-Path $repository 'scripts/Test-PreviewDistribution.Inputs.ps1'
if (-not (Test-Path -LiteralPath $inputHelper -PathType Leaf)) { throw "Missing integrated synthetic input helper: $inputHelper" }
. $inputHelper
. (Join-Path $repository 'scripts/PackageDistribution.Common.ps1')

# This harness only generates opaque input packages and copies their bytes into
# synthetic directories. Install.ps1, native programs and game markers are never run.
function Check-Acceptance([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-AcceptanceHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}
function Read-AcceptanceJson([string]$Path) {
    Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable -Depth 48
}
function Write-AcceptanceJson([string]$Root, [string]$Path, $Value) {
    Write-PreviewInputFile $Root $Path (ConvertTo-Json -InputObject $Value -Depth 40)
}
function Write-AcceptanceBytes([string]$Root, [string]$Path, [byte[]]$Bytes) {
    $safe = Assert-PreviewPathInside $Root $Path
    New-Item -ItemType Directory -Path (Split-Path -Parent $safe) -Force | Out-Null
    [IO.File]::WriteAllBytes($safe, $Bytes)
}
function Assert-AcceptanceSet([string[]]$Actual, [string[]]$Expected, [string]$Message) {
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $Actual) { Check-Acceptance ($set.Add($item)) "$Message Duplicate: $item" }
    Check-Acceptance ($set.Count -eq $Expected.Count) "$Message Expected $($Expected.Count), got $($set.Count)."
    foreach ($item in $Expected) { Check-Acceptance ($set.Contains($item)) "$Message Missing: $item" }
}
function Assert-AcceptancePolicy($Value, [switch]$ProgramsExecuted) {
    Check-Acceptance ($Value.acceptance -ceq 'preview/unverified') 'Evidence upgraded preview acceptance.'
    Check-Acceptance ($Value.productionAdmission -is [bool] -and -not $Value.productionAdmission) 'Evidence opened production admission.'
    if ($Value.Contains('realMachineAcceptanceVerified')) {
        Check-Acceptance ($Value.realMachineAcceptanceVerified -is [bool] -and -not $Value.realMachineAcceptanceVerified) 'Read-only evidence claims real-machine acceptance.'
    }
    if ($ProgramsExecuted) {
        Check-Acceptance ($Value.programsExecuted -is [bool] -and -not $Value.programsExecuted) 'Read-only evidence claims program execution.'
    }
}

$subjectNames = @('PackageDistribution.Common.ps1','Install.Common.ps1','Install.ps1','Uninstall.ps1',
    'Collect-DistributionEvidence.ps1','New-PreviewAcceptanceRecord.ps1','Test-PreviewAcceptanceRecord.ps1')
$sourceIdentities = foreach ($subjectName in $subjectNames) {
    $path = Join-Path $repository "scripts/$subjectName"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing test subject: $path" }
    [ordered]@{path=$path;sha256=(Get-AcceptanceHash $path)}
}
$testIdentities = @(
    [ordered]@{path=$PSCommandPath;sha256=(Get-AcceptanceHash $PSCommandPath)}
    [ordered]@{path=$inputHelper;sha256=(Get-AcceptanceHash $inputHelper)}
)
$runId = [Guid]::NewGuid().ToString('N')
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repository "artifacts/preview-acceptance-tests-$runId" }
$evidenceRoot = [IO.Path]::TrimEndingDirectorySeparator($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceDirectory))
$fixtureRoot = Join-Path $evidenceRoot 'fixtures'
$resultPath = Join-Path $evidenceRoot 'results.json'
$null = Assert-PreviewPathInside $evidenceRoot $fixtureRoot
if (Test-Path -LiteralPath $evidenceRoot) { throw 'Choose a fresh EvidenceDirectory; reports and fixtures are never reused.' }
New-Item -ItemType Directory -Path $evidenceRoot -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $fixtureRoot -ErrorAction Stop | Out-Null
$results = [Collections.Generic.List[object]]::new()
$fixtures = [Collections.Generic.List[object]]::new()
$script:acceptanceMessages = [Collections.Generic.List[string]]::new()
$startedAt = [DateTimeOffset]::UtcNow

function Get-AcceptanceFileStamp([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    return "$(Get-AcceptanceHash $Path)|$($item.Length)|$($item.CreationTimeUtc.ToString('o'))|$($item.LastWriteTimeUtc.ToString('o'))"
}
function Get-AcceptanceTreeStamp([string]$Root) {
    $snapshot = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction Stop) {
        $path = Assert-PreviewPathInside $Root $item.FullName
        if (-not $item.PSIsContainer) { $snapshot.Add($path, (Get-AcceptanceFileStamp $path)) }
    }
    return ,$snapshot
}
function Assert-AcceptanceTreeStamp([string]$Root, $Expected, [string]$Message) {
    $actual = Get-AcceptanceTreeStamp $Root
    Assert-AcceptanceSet @($actual.Keys) @($Expected.Keys) $Message
    foreach ($path in $Expected.Keys) { Check-Acceptance ($actual[$path] -ceq $Expected[$path]) "$Message Changed source identity: $path" }
}
function Get-AcceptanceSourcesStamp($Fixture) {
    [ordered]@{
        repository=(Get-AcceptanceTreeStamp $Fixture.RepoRoot)
        game=(Get-AcceptanceTreeStamp $Fixture.Game)
        inputs=(Get-AcceptanceTreeStamp (Join-Path $Fixture.Root 'inputs'))
        outside=(Get-AcceptanceFileStamp (Join-Path $Fixture.Root 'outside-game/canary.txt'))
    }
}
function Assert-AcceptanceSourcesStamp($Fixture, $Expected) {
    Assert-AcceptanceTreeStamp $Fixture.RepoRoot $Expected.repository 'Collector or validator changed the synthetic repository.'
    Assert-AcceptanceTreeStamp $Fixture.Game $Expected.game 'Collector or validator changed installed or original synthetic game bytes.'
    Assert-AcceptanceTreeStamp (Join-Path $Fixture.Root 'inputs') $Expected.inputs 'Collector or validator changed declaration, selected or evidence inputs.'
    Check-Acceptance ((Get-AcceptanceFileStamp (Join-Path $Fixture.Root 'outside-game/canary.txt')) -ceq $Expected.outside) 'Collector or validator changed the outside-game canary.'
}

function Publish-AcceptanceFixture($Fixture, [string]$Revision) {
    Set-PreviewDistributionInputs $Fixture $Revision
    $Fixture.Distribution = New-RuntimeDistribution -RepoRoot $Fixture.RepoRoot -PackageKind 'Runtime' `
        -NativeOutputDirectory $Fixture.NativeOutputDirectory -DoorstopOutput $Fixture.DoorstopOutput `
        -EngineKernelOutput $Fixture.EngineKernelOutput -PlaybackRuntimeOutput $Fixture.PlaybackRuntimeOutput `
        -OutputDirectory $Fixture.ArtifactRoot -SdkDirectory $Fixture.SdkDirectory -BasicPluginIds $Fixture.BasicPluginIds
}
function Set-AcceptanceInstalledBytes($Fixture) {
    $manifest = Read-AcceptanceJson $Fixture.Distribution.ManifestPath
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($entry in $manifest.files) {
        $source = Assert-PreviewPathInside $Fixture.Root (Join-Path $Fixture.Distribution.PackageRoot $entry.path)
        $target = Assert-PreviewPathInside $Fixture.Root (Join-Path $Fixture.Game $entry.path)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [IO.File]::Copy($source, $target, $false)
        $rows.Add([ordered]@{path=$entry.path.Replace('/','\');sha256=$entry.sha256})
    }
    foreach ($relative in @('BepInEx/core/dobby.dll','dotnet/runtime.dll','dotnet/host/hostfxr.dll','doorstop_config.ini')) {
        $source = Assert-PreviewPathInside $Fixture.Root (Join-Path $Fixture.RepoRoot ".tools/bepinex-789/$relative")
        $target = Assert-PreviewPathInside $Fixture.Root (Join-Path $Fixture.Game $relative)
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        [IO.File]::Copy($source, $target, $false)
        $rows.Add([ordered]@{path=$relative.Replace('/','\');sha256=(Get-AcceptanceHash $target)})
    }
    $configuration = 'BepInEx/RizToolbox/runtime.json'
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.Game $configuration) '{"schemaVersion":1,"mode":"Standard","logLevel":"Info","enableProofPlugins":false,"fullAuditEachTick":false}'
    $rows.Add([ordered]@{path=$configuration.Replace('/','\');sha256=(Get-AcceptanceHash (Join-Path $Fixture.Game $configuration))})
    Write-PreviewInputFile $Fixture.Root (Join-Path $Fixture.Game 'BepInEx/RizToolbox/plugins/user.fixture/preferences.json') '{"preserve":"synthetic user bytes"}'
    # This is a synthetic ownership map, not an installer result or proof of startup.
    Write-AcceptanceJson $Fixture.Root $Fixture.InstallationManifest ([ordered]@{
        schemaVersion=1;owner='riz_toolbox';gameRoot=$Fixture.Game;version='synthetic';files=$rows.ToArray();pending=@()
    })
}
function New-AcceptanceFixture([string]$Name, [string[]]$BasicPluginIds = @(), [switch]$WithInstalled) {
    $fixture = New-PreviewFixture -FixtureRoot $fixtureRoot -RepositoryRoot $repository -Name $Name -PackageKind 'Runtime' -BasicPluginIds $BasicPluginIds
    $fixtures.Add($fixture)
    foreach ($subjectName in @('New-PreviewAcceptanceRecord.ps1','Test-PreviewAcceptanceRecord.ps1')) {
        Microsoft.PowerShell.Management\Copy-Item -LiteralPath (Join-Path $repository "scripts/$subjectName") -Destination (Join-Path $fixture.RepoRoot "scripts/$subjectName") -ErrorAction Stop
    }
    $fixture | Add-Member -NotePropertyName DeclarationPath -NotePropertyValue (Join-Path $fixture.Root 'inputs/declaration.json')
    $fixture | Add-Member -NotePropertyName OutputParent -NotePropertyValue (Join-Path $fixture.Root 'evidence')
    $fixture | Add-Member -NotePropertyName InstalledRequested -NotePropertyValue ([bool]$WithInstalled)
    $fixture | Add-Member -NotePropertyName BasicPluginFiles -NotePropertyValue @()
    $fixture | Add-Member -NotePropertyName OwnedWavFiles -NotePropertyValue @()
    New-Item -ItemType Directory -Path $fixture.OutputParent | Out-Null
    Publish-AcceptanceFixture $fixture 'v1'
    Write-AcceptanceJson $fixture.Root $fixture.DeclarationPath ([ordered]@{
        schemaVersion=1;session=[ordered]@{id="synthetic-$Name";kind='unverified';runtimeMode='Standard'};evidence=@()
    })
    if ($WithInstalled) { Set-AcceptanceInstalledBytes $fixture }
    return $fixture
}

function Assert-AcceptanceNoSuccess {
    foreach ($message in $script:acceptanceMessages) {
        Check-Acceptance (-not $message.StartsWith('Preview record collected;', [StringComparison]::Ordinal) -and
            -not $message.StartsWith('Record and current source bytes are consistent.', [StringComparison]::Ordinal)) 'Failed operation emitted a success message.'
    }
}
function Expect-AcceptanceFailure([scriptblock]$Action, [string[]]$Contains = @()) {
    $script:acceptanceMessages.Clear()
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
    Check-Acceptance ($null -ne $failure) 'Expected rejection, but the operation succeeded.'
    foreach ($fragment in $Contains) {
        Check-Acceptance ($failure.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) "Expected '$fragment'; got '$failure'."
    }
    Assert-AcceptanceNoSuccess
    return $failure
}
function Invoke-AcceptanceCollect($Fixture, [string]$Output, [string]$ManifestPath) {
    if (-not $Output) { $Output = Join-Path $Fixture.OutputParent 'collection' }
    if (-not $ManifestPath) { $ManifestPath = $Fixture.Distribution.ManifestPath }
    $parameters = @{
        RepositoryRoot=$Fixture.RepoRoot;PackageRoot=$Fixture.Distribution.PackageRoot;ManifestPath=$ManifestPath
        DeclarationPath=$Fixture.DeclarationPath;OutputDirectory=$Output
        BasicPluginFiles=@($Fixture.BasicPluginFiles);OwnedWavFiles=@($Fixture.OwnedWavFiles)
    }
    if ($Fixture.InstalledRequested) { $parameters.InstalledRoot = $Fixture.Game }
    $script:acceptanceMessages.Clear()
    & (Join-Path $Fixture.RepoRoot 'scripts/New-PreviewAcceptanceRecord.ps1') @parameters |
        ForEach-Object { $script:acceptanceMessages.Add([string]$_) }
    return $Output
}
function Invoke-AcceptanceValidate($Fixture, [string]$Bundle, [string]$Output, [string]$ManifestPath) {
    if (-not $Output) { $Output = Join-Path $Fixture.OutputParent 'validation' }
    if (-not $ManifestPath) { $ManifestPath = $Fixture.Distribution.ManifestPath }
    $parameters = @{
        RecordPath=(Join-Path $Bundle 'acceptance-record.json');RepositoryRoot=$Fixture.RepoRoot
        PackageRoot=$Fixture.Distribution.PackageRoot;ManifestPath=$ManifestPath;OutputDirectory=$Output
    }
    if ($Fixture.InstalledRequested) { $parameters.InstalledRoot = $Fixture.Game }
    $script:acceptanceMessages.Clear()
    & (Join-Path $Fixture.RepoRoot 'scripts/Test-PreviewAcceptanceRecord.ps1') @parameters |
        ForEach-Object { $script:acceptanceMessages.Add([string]$_) }
    return $Output
}
function Assert-AcceptanceCollection($Fixture, [string]$Output) {
    Assert-AcceptanceSet @((Get-ChildItem -LiteralPath $Output -Force).Name) @('collection-start.json','declaration.json','acceptance-record.json','collection-complete.json') 'Unexpected collection bundle.'
    $record = Read-AcceptanceJson (Join-Path $Output 'acceptance-record.json')
    $seal = Read-AcceptanceJson (Join-Path $Output 'collection-complete.json')
    $start = Read-AcceptanceJson (Join-Path $Output 'collection-start.json')
    Check-Acceptance ($record.kind -ceq 'RizToolbox.PreviewAcceptanceRecord' -and $record.collectionStatus -ceq 'snapshot-awaiting-final-source-seal') 'Unexpected collection snapshot identity.'
    Assert-AcceptancePolicy $record -ProgramsExecuted
    Assert-AcceptancePolicy $record.sources.assessment
    Assert-AcceptancePolicy $seal
    Assert-AcceptancePolicy $start
    Check-Acceptance ($seal.recordId -ceq $record.recordId -and $seal.recordSha256 -ceq (Get-AcceptanceHash (Join-Path $Output 'acceptance-record.json'))) 'Collection seal lost the saved record identity.'
    Check-Acceptance ($seal.sourceConsistency -ceq 'consistent-at-collection-finish' -and
        $seal.declarationSha256 -ceq (Get-AcceptanceHash $Fixture.DeclarationPath) -and
        $seal.startSha256 -ceq (Get-AcceptanceHash (Join-Path $Output 'collection-start.json'))) 'Collection seal lost source or start identity.'
    Check-Acceptance ((Get-AcceptanceHash (Join-Path $Output 'declaration.json')) -ceq (Get-AcceptanceHash $Fixture.DeclarationPath)) 'The declaration copy changed its original bytes.'
    $manifest = Read-AcceptanceJson $Fixture.Distribution.ManifestPath
    Assert-AcceptanceSet @($record.sources.candidate.basicPlugins) @($Fixture.BasicPluginIds) 'Candidate basic selection changed.'
    Assert-AcceptanceSet @($record.sources.candidate.payload.relativePath) @(Get-PreviewExpectedRuntimePaths $Fixture.BasicPluginIds) 'Candidate payload boundary changed.'
    Check-Acceptance ($record.sources.candidate.manifest.sha256 -ceq (Get-AcceptanceHash $Fixture.Distribution.ManifestPath)) 'Candidate manifest identity is stale.'
    foreach ($entry in $manifest.files) {
        $rows = @($record.sources.candidate.payload | Where-Object { $_.relativePath -ceq $entry.path })
        Check-Acceptance ($rows.Count -eq 1 -and $rows[0].identity.sha256 -ceq $entry.sha256) "Payload snapshot mismatch: $($entry.path)"
    }
    return $record
}
function Assert-AcceptanceValidation([string]$Bundle, [string]$Output) {
    Assert-AcceptanceSet @((Get-ChildItem -LiteralPath $Output -Force).Name) @('validation-start.json','validation-result.json') 'Unexpected validation bundle.'
    $report = Read-AcceptanceJson (Join-Path $Output 'validation-result.json')
    Assert-AcceptancePolicy $report -ProgramsExecuted
    Assert-AcceptancePolicy $report.reportedAssessment
    Check-Acceptance ($report.kind -ceq 'RizToolbox.PreviewRecordValidation' -and
        $report.sourceConsistency -ceq 'consistent-at-validation-finish' -and
        $report.recordSha256 -ceq (Get-AcceptanceHash (Join-Path $Bundle 'acceptance-record.json'))) 'Validator did not identify the current saved record.'
    return $report
}
function Assert-AcceptanceFailureBundle([string]$Output, [ValidateSet('collection','validation')][string]$Operation) {
    Check-Acceptance (Test-Path -LiteralPath (Join-Path $Output "$Operation-failure.json") -PathType Leaf) 'Failure bundle lost its failure report.'
    $failure = Read-AcceptanceJson (Join-Path $Output "$Operation-failure.json")
    Assert-AcceptancePolicy $failure
    Check-Acceptance ($failure.kind -ceq 'RizToolbox.PreviewEvidenceFailure' -and $failure.status -ceq 'failed' -and $failure.error) 'Failure report does not retain the error.'
    if ($Operation -eq 'collection') { Check-Acceptance (-not (Test-Path -LiteralPath (Join-Path $Output 'collection-complete.json'))) 'Failed collection wrote a completion seal.' }
    else { Check-Acceptance (-not (Test-Path -LiteralPath (Join-Path $Output 'validation-result.json'))) 'Failed validation wrote a success report.' }
}
function Get-AcceptanceInstalledRow($Record, [string]$Relative) {
    $rows = @($Record.sources.installed.files | Where-Object { $_.relativePath -ceq $Relative })
    Check-Acceptance ($rows.Count -eq 1) "Missing or duplicate installed row: $Relative"
    return $rows[0]
}

function New-AcceptanceBoundDeclaration($Fixture, [switch]$Complete) {
    # These are fabricated declarations for binding/policy verification only.
    # The harness never calls them a measured or executed real-machine session.
    $start = [DateTimeOffset]::UtcNow.AddMinutes(-3).ToString('o')
    $end = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
    $captured = [DateTimeOffset]::UtcNow.AddMinutes(-2).ToString('o')
    $sessionId = "synthetic-bound-$($Fixture.Name)"
    $manifestHash = Get-AcceptanceHash $Fixture.Distribution.ManifestPath
    $currentPath = Join-Path $Fixture.Root 'inputs/current-report.json'
    $historyPath = Join-Path $Fixture.Root 'inputs/historical-report.json'
    $baselinePath = Join-Path $Fixture.Root 'inputs/original-report.json'
    foreach ($path in @($currentPath,$historyPath,$baselinePath)) {
        Write-AcceptanceJson $Fixture.Root $path ([ordered]@{kind='synthetic-offline-report';passed=$true;programsExecuted=$false;detail='Fabricated bytes; no real-machine observation.'})
    }
    $declaration = [ordered]@{
        schemaVersion=1
        session=[ordered]@{id=$sessionId;kind='current-real-machine';machineId='synthetic-machine';runtimeMode='Standard';scene='synthetic-menu';gameVersion='synthetic';operator='offline-harness';startedUtc=$start;endedUtc=$end;manifestSha256=$manifestHash}
        evidence=@(
            [ordered]@{id='current-report';path=$currentPath;role='current-candidate';capturedUtc=$captured;sessionId=$sessionId;manifestSha256=$manifestHash;machineId='synthetic-machine';scene='synthetic-menu';runtimeMode='Standard';description='Synthetic current binding';declaredSha256=(Get-AcceptanceHash $currentPath)}
            [ordered]@{id='historical-report';path=$historyPath;role='historical-context';capturedUtc=$captured;sessionId=$sessionId;manifestSha256=('0' * 64);machineId='synthetic-machine';scene='synthetic-menu';runtimeMode='Standard';description='Synthetic historical context';declaredSha256=(Get-AcceptanceHash $historyPath)}
            [ordered]@{id='original-report';path=$baselinePath;role='original-baseline';capturedUtc=$captured;sessionId='synthetic-original';machineId='synthetic-machine';scene='synthetic-menu';runtimeMode='Original';description='Synthetic baseline';declaredSha256=(Get-AcceptanceHash $baselinePath)}
        )
    }
    if ($Complete) {
        $declaration.scenarios = [ordered]@{}
        $declaration.checks = [ordered]@{}
        foreach ($key in @('song-score-panel','color-theme-restore','shortcut-panel-owned-wav')) {
            $declaration.scenarios[$key] = [ordered]@{status='passed';detail='Fabricated for acceptance policy verification';evidenceIds=@('current-report')}
        }
        foreach ($key in @('installation','disable-restoration','restart-config','plugin-coexistence','fault-recovery','cost')) {
            $declaration.checks[$key] = [ordered]@{status='passed';detail='Fabricated for acceptance policy verification';evidenceIds=@('current-report')}
        }
        $declaration.cost = [ordered]@{}
        foreach ($name in @('standardEmptyMods','basicScenarios')) {
            $declaration.cost[$name] = [ordered]@{
                sessionId=$sessionId;machineId='synthetic-machine';scene='synthetic-menu';runtimeMode='Standard';startedUtc=$start;endedUtc=$end
                measurementMethod='Fabricated metrics; no execution';recoveryDetail='Fabricated restoration note'
                warmedUpSeconds=10;durationSeconds=60;pluginCount=$(if ($name -eq 'standardEmptyMods') { 0 } else { $Fixture.BasicPluginIds.Count })
                managedP95Ms=0.1;managedP99Ms=0.2;steadyAttributableMaxMs=0.3;frameIntervalP95Ms=16;frameIntervalP99Ms=17
                transitionMaxMs=0.5;scanMaxMs=0.4;uploadMaxMs=0.4;controlMaxMs=0.4
                includes=@('entry','host');excludes=@('game-original');evidenceIds=@('current-report')
            }
        }
        $declaration.cost.originalBaseline = [ordered]@{
            sessionId='synthetic-original';machineId='synthetic-machine';scene='synthetic-menu';runtimeMode='Original';startedUtc=$start;endedUtc=$end
            measurementMethod='Fabricated equivalent baseline';warmedUpSeconds=10;durationSeconds=60;pluginCount=0
            frameIntervalP95Ms=16;frameIntervalP99Ms=17;includes=@('game-original');excludes=@();evidenceIds=@('original-report')
        }
        $wav = Join-Path $Fixture.Root 'inputs/owned-fixture.wav'
        Write-AcceptanceBytes $Fixture.Root $wav ([Text.UTF8Encoding]::new($false).GetBytes('Synthetic WAV identity marker; never played.'))
        $Fixture.OwnedWavFiles = @($wav)
        $declaration.ownedWavStatement = 'The test owns these synthetic marker bytes. No real audio or scene was executed.'
    }
    return $declaration
}

function Invoke-AcceptanceCase([string]$Name, [scriptblock]$Action) {
    if ($Name -notlike $Case) { return }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action | Out-Null
        $results.Add([ordered]@{name=$Name;passed=$true;durationMs=$timer.ElapsedMilliseconds})
        Write-Output "PASS $Name"
    } catch {
        $results.Add([ordered]@{name=$Name;passed=$false;durationMs=$timer.ElapsedMilliseconds;error=$_.Exception.Message;stack=$_.ScriptStackTrace})
        Write-Output "FAIL $Name : $($_.Exception.Message)"
    } finally { $timer.Stop(); $script:acceptanceMessages.Clear() }
}

try {
    Invoke-AcceptanceCase 'empty-basic-untested-record-and-validation' {
        $f = New-AcceptanceFixture 'empty-basic'
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        Check-Acceptance ($record.sources.candidate.basicPlugins.Count -eq 0 -and $record.sources.selected.basicPluginFiles.Count -eq 0) 'Empty basic selection acquired plugins.'
        Check-Acceptance ($record.sources.installed.root -eq $null -and $record.sources.installed.files.Count -eq 0 -and $record.sources.installed.ownershipManifest -eq $null) 'Unrequested installed comparison acquired ownership facts.'
        Check-Acceptance ($record.sources.assessment.state -ceq 'incomplete' -and 'installed-payload.not-requested' -cin $record.sources.assessment.uncoveredItems) 'Untested collection was treated as complete.'
        foreach ($group in @('scenarios','checks','experimental')) {
            foreach ($outcome in $record.sources.declaration.content[$group].Values) { Check-Acceptance ($outcome.status -ceq 'untested') 'Omitted outcome became passed.' }
        }
        $validation = Invoke-AcceptanceValidate $f $bundle
        $report = Assert-AcceptanceValidation $bundle $validation
        Check-Acceptance ($report.reportedAssessment.state -ceq 'incomplete') 'Validation promoted an untested declaration.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'all-basic-matched-installed-and-owned-config' {
        $f = New-AcceptanceFixture 'all-basic-matched' @(Get-PreviewBasicPluginIds) -WithInstalled
        $f.BasicPluginFiles = @($f.BasicPluginIds | ForEach-Object { Join-Path $f.Game "BepInEx/RizToolbox/mods/$_/plugin.json" })
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        Assert-AcceptanceSet @($record.sources.installed.files.relativePath) @(Get-PreviewExpectedRuntimePaths $f.BasicPluginIds) 'Installed comparison omitted selected basic files.'
        foreach ($row in $record.sources.installed.files) { Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'matched') 'Matching installed bytes or ownership were not recorded.' }
        Check-Acceptance ($record.sources.installed.scope -ceq 'candidate-payload-and-recorded-ownership' -and $record.sources.installed.ownershipManifest.state -ceq 'finalized') 'Finalized ownership was not recorded separately.'
        $ownership = $record.sources.installed.ownershipManifest
        Check-Acceptance ($ownership.path -ceq $f.InstallationManifest -and $ownership.identity.sha256 -ceq (Get-AcceptanceHash $f.InstallationManifest)) 'Ownership manifest identity changed.'
        $commonTool = @($record.sources.tools | Where-Object { $_.path -ceq (Join-Path $f.RepoRoot 'scripts/Install.Common.ps1') })
        Check-Acceptance ($commonTool.Count -eq 1 -and $commonTool[0].sha256 -ceq (Get-AcceptanceHash (Join-Path $f.RepoRoot 'scripts/Install.Common.ps1'))) 'Ownership helper identity was omitted from recorded tools.'
        $config = @($ownership.files | Where-Object { $_.relativePath -ceq 'BepInEx/RizToolbox/runtime.json' })
        Check-Acceptance ($config.Count -eq 1 -and $config[0].status -ceq 'matched' -and -not $config[0].requiredForCandidate) 'Owned configuration outside candidate payload was omitted or made mandatory.'
        foreach ($relative in @('BepInEx/core/dobby.dll','dotnet/runtime.dll','dotnet/host/hostfxr.dll','doorstop_config.ini')) {
            $required = @($ownership.files | Where-Object { $_.relativePath -ceq $relative })
            Check-Acceptance ($required.Count -eq 1 -and $required[0].status -ceq 'matched' -and $required[0].requiredForCandidate) "Required owned dependency was not identified: $relative"
        }
        Check-Acceptance ($record.sources.selected.basicPluginFiles.Count -eq 3) 'Explicit selected basic source files were lost.'
        $null = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'installed-missing-is-recorded-as-failed' {
        $f = New-AcceptanceFixture 'installed-missing' -WithInstalled
        $relative = 'BepInEx/plugins/RizToolbox/RizToolbox.Core.dll'
        Remove-PreviewFixturePath $f.Root (Join-Path $f.Game $relative)
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        $row = Get-AcceptanceInstalledRow $record $relative
        Check-Acceptance ($row.status -ceq 'missing' -and $row.actualType -ceq 'missing' -and $row.actual -eq $null -and $row.ownership -ceq 'matched') 'Missing installed bytes were hidden by matching recorded ownership.'
        Check-Acceptance ($record.sources.assessment.state -ceq 'failed' -and "installed-payload.$relative.missing" -cin $record.sources.assessment.failedItems) 'Missing installed payload did not fail reported assessment.'
        $report = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Check-Acceptance ($report.reportedAssessment.state -ceq 'failed') 'Consistent missing-source snapshot was relabeled as acceptance.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'installed-mismatch-and-owned-config-drift-are-recorded' {
        $f = New-AcceptanceFixture 'installed-mismatched' -WithInstalled
        $relative = 'BepInEx/plugins/RizToolbox/RizToolbox.Core.dll'
        Write-PreviewInputFile $f.Root (Join-Path $f.Game $relative) 'Changed synthetic installed payload.'
        Write-PreviewInputFile $f.Root (Join-Path $f.Game 'BepInEx/RizToolbox/runtime.json') '{"mode":"Developer","custom":"user modification"}'
        $dependency = 'dotnet/runtime.dll'
        Write-PreviewInputFile $f.Root (Join-Path $f.Game $dependency) 'Changed synthetic required dependency.'
        $ownership = Read-AcceptanceJson $f.InstallationManifest
        $unowned = 'BepInEx/plugins/RizToolbox/RizToolbox.Hosting.dll'
        $ownership.files = @($ownership.files | Where-Object { $_.path.Replace('\','/') -cne $unowned })
        $hashMismatch = 'BepInEx/plugins/RizToolbox/RizToolbox.Adapter.dll'
        foreach ($entry in $ownership.files) { if ($entry.path.Replace('\','/') -ceq $hashMismatch) { $entry.sha256 = '0' * 64 } }
        Write-AcceptanceJson $f.Root $f.InstallationManifest $ownership
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        $row = Get-AcceptanceInstalledRow $record $relative
        Check-Acceptance ($row.status -ceq 'mismatched' -and $row.ownership -ceq 'matched' -and $row.actual.sha256 -ceq (Get-AcceptanceHash (Join-Path $f.Game $relative))) 'Installed byte drift was not reported independently from ownership.'
        $row = Get-AcceptanceInstalledRow $record $unowned
        Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'unowned') 'Matching bytes were adopted into missing ownership.'
        $row = Get-AcceptanceInstalledRow $record $hashMismatch
        Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'mismatched') 'Forged recorded hash was ignored.'
        $config = @($record.sources.installed.ownershipManifest.files | Where-Object { $_.relativePath -ceq 'BepInEx/RizToolbox/runtime.json' })
        Check-Acceptance ($config.Count -eq 1 -and $config[0].status -ceq 'mismatched' -and -not $config[0].requiredForCandidate) 'Modified owned configuration disappeared or became required.'
        Check-Acceptance ('owned-installation-file.BepInEx/RizToolbox/runtime.json.mismatched' -cnotin $record.sources.assessment.failedItems) 'User configuration modification was treated as a required payload failure.'
        $required = @($record.sources.installed.ownershipManifest.files | Where-Object { $_.relativePath -ceq $dependency })
        Check-Acceptance ($required.Count -eq 1 -and $required[0].requiredForCandidate -and $required[0].status -ceq 'mismatched' -and
            "owned-installation-file.$dependency.mismatched" -cin $record.sources.assessment.failedItems) 'Required dependency drift outside candidate payload did not fail reported assessment.'
        $report = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Check-Acceptance ($report.reportedAssessment.state -ceq 'failed') 'Validator ignored recorded mismatches.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'fresh-output-and-create-new-preserve-existing-bytes' {
        $f = New-AcceptanceFixture 'create-new'
        $canary = Join-Path $f.Root 'inputs/create-new-canary.bin'
        Write-PreviewInputFile $f.Root $canary 'Existing bytes must never be overwritten.'
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $null = Assert-AcceptanceCollection $f $bundle
        $bundleBefore = Get-AcceptanceTreeStamp $bundle
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceCollect $f $bundle } @('fresh evidence output directory')
        Assert-AcceptanceTreeStamp $bundle $bundleBefore 'Repeated collection changed saved evidence.'
        $validation = Invoke-AcceptanceValidate $f $bundle
        $null = Assert-AcceptanceValidation $bundle $validation
        $validationBefore = Get-AcceptanceTreeStamp $validation
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $validation } @('fresh evidence output directory')
        Assert-AcceptanceTreeStamp $validation $validationBefore 'Repeated validation changed saved evidence.'
        $library = Join-Path $f.RepoRoot 'scripts/Test-PreviewAcceptanceRecord.ps1'
        $null = Expect-AcceptanceFailure {
            & {
                param($LibraryPath,$ExistingPath)
                . $LibraryPath -LibraryOnly
                Write-PreviewNewBytes $ExistingPath ([byte[]]@(9,8,7))
            } $library $canary
        }
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'collector-and-validator-output-isolation' {
        $f = New-AcceptanceFixture 'output-isolation' -WithInstalled
        $before = Get-AcceptanceSourcesStamp $f
        foreach ($output in @((Join-Path $f.Distribution.PackageRoot 'evidence'),(Join-Path $f.Game 'evidence'))) {
            $null = Expect-AcceptanceFailure { Invoke-AcceptanceCollect $f $output } @('overlaps a read directory')
            Check-Acceptance (-not (Test-Path -LiteralPath $output)) 'Collector created output inside a read source.'
        }
        foreach ($output in @((Join-Path $f.Root 'inputs'),(Join-Path $f.RepoRoot 'scripts'))) {
            $null = Expect-AcceptanceFailure { Invoke-AcceptanceCollect $f $output } @('contains a source file')
        }
        $bundle = Invoke-AcceptanceCollect $f
        $null = Assert-AcceptanceCollection $f $bundle
        $bundleBefore = Get-AcceptanceTreeStamp $bundle
        foreach ($output in @((Join-Path $bundle 'validation'),(Join-Path $f.Distribution.PackageRoot 'validation'),(Join-Path $f.Game 'validation'))) {
            $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $output } @('overlaps a read directory')
            Check-Acceptance (-not (Test-Path -LiteralPath $output)) 'Validator created output inside a read source.'
        }
        Assert-AcceptanceTreeStamp $bundle $bundleBefore 'Rejected validation changed its original bundle.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'historical-record-refuses-new-current-manifest' {
        $f = New-AcceptanceFixture 'stale-current-manifest'
        $bundle = Invoke-AcceptanceCollect $f
        $null = Assert-AcceptanceCollection $f $bundle
        $oldBundle = Get-AcceptanceTreeStamp $bundle
        Publish-AcceptanceFixture $f 'v2'
        $before = Get-AcceptanceSourcesStamp $f
        $validation = Join-Path $f.OutputParent 'validation'
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $validation } @('sources')
        Assert-AcceptanceFailureBundle $validation 'validation'
        Assert-AcceptanceTreeStamp $bundle $oldBundle 'Stale validation changed historical evidence.'
        Assert-AcceptanceSourcesStamp $f $before
        $history = @(Get-ChildItem -LiteralPath (Join-Path $f.ArtifactRoot 'package-history') -Directory -Filter 'runtime-*')
        Check-Acceptance ($history.Count -eq 1) 'Republished fixture lost its generated historical package.'
        $oldManifest = Join-Path $history[0].FullName 'package-manifest.json'
        $oldManifestOutput = Join-Path $f.OutputParent 'old-manifest-validation'
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $oldManifestOutput $oldManifest } @('explicitly selected current candidate')
        Check-Acceptance (-not (Test-Path -LiteralPath $oldManifestOutput)) 'Old manifest identity rejection created a validation directory.'
    }

    Invoke-AcceptanceCase 'source-byte-and-identity-drift-refuse-validation' {
        foreach ($drift in @('payload','identity','evidence','ownership')) {
            $f = New-AcceptanceFixture "source-drift-$drift"
            if ($drift -eq 'ownership') { $f.InstalledRequested = $true; Set-AcceptanceInstalledBytes $f }
            if ($drift -eq 'evidence') {
                $declaration = New-AcceptanceBoundDeclaration $f
                Write-AcceptanceJson $f.Root $f.DeclarationPath $declaration
            }
            $bundle = Invoke-AcceptanceCollect $f
            $null = Assert-AcceptanceCollection $f $bundle
            $bundleBefore = Get-AcceptanceTreeStamp $bundle
            if ($drift -eq 'payload') {
                Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot 'winhttp.dll') 'Tampered generated runtime payload.'
            } elseif ($drift -eq 'identity') {
                $originalHash = Get-AcceptanceHash $f.DeclarationPath
                $written = [IO.File]::GetLastWriteTimeUtc($f.DeclarationPath)
                [IO.File]::SetLastWriteTimeUtc($f.DeclarationPath, $written.AddSeconds(5))
                Check-Acceptance ((Get-AcceptanceHash $f.DeclarationPath) -ceq $originalHash) 'Identity drift changed bytes instead of only the recorded timestamp.'
            } elseif ($drift -eq 'evidence') {
                Write-PreviewInputFile $f.Root (Join-Path $f.Root 'inputs/current-report.json') 'Changed synthetic external evidence.'
            } else {
                $ownership = Read-AcceptanceJson $f.InstallationManifest
                $ownership.files = @($ownership.files | Where-Object { $_.path.Replace('\','/') -cne 'BepInEx/plugins/RizToolbox/RizToolbox.Hosting.dll' })
                Write-AcceptanceJson $f.Root $f.InstallationManifest $ownership
            }
            $before = Get-AcceptanceSourcesStamp $f
            $validation = Join-Path $f.OutputParent 'validation'
            $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $validation }
            Assert-AcceptanceFailureBundle $validation 'validation'
            Assert-AcceptanceTreeStamp $bundle $bundleBefore 'Drift rejection changed preserved evidence.'
            Assert-AcceptanceSourcesStamp $f $before
        }
    }

    Invoke-AcceptanceCase 'collection-failure-bundle-is-preserved-and-never-complete' {
        $f = New-AcceptanceFixture 'failed-collection'
        Write-PreviewInputFile $f.Root (Join-Path $f.Distribution.PackageRoot 'winhttp.dll') 'Tampered before real collection.'
        $before = Get-AcceptanceSourcesStamp $f
        $failedBundle = Join-Path $f.OutputParent 'failed-collection'
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceCollect $f $failedBundle } @('Package hash mismatch')
        Assert-AcceptanceFailureBundle $failedBundle 'collection'
        Assert-AcceptanceSet @((Get-ChildItem -LiteralPath $failedBundle -Force).Name) @('collection-start.json','collection-failure.json') 'Failed collection created an apparent record or completion.'
        Assert-AcceptanceSourcesStamp $f $before
        $failedBefore = Get-AcceptanceTreeStamp $failedBundle
        Publish-AcceptanceFixture $f 'v2'
        $bundle = Invoke-AcceptanceCollect $f
        $null = Assert-AcceptanceCollection $f $bundle
        # A saved record without its final seal is still an incomplete bundle.
        Remove-PreviewFixturePath $f.Root (Join-Path $bundle 'collection-complete.json')
        $incompleteBefore = Get-AcceptanceTreeStamp $bundle
        $before = Get-AcceptanceSourcesStamp $f
        $validation = Join-Path $f.OutputParent 'validation'
        $null = Expect-AcceptanceFailure { Invoke-AcceptanceValidate $f $bundle $validation } @('bundle is incomplete')
        Assert-AcceptanceFailureBundle $validation 'validation'
        Assert-AcceptanceTreeStamp $failedBundle $failedBefore 'Later attempts overwrote the real failure bundle.'
        Assert-AcceptanceTreeStamp $bundle $incompleteBefore 'Validator repaired or sealed an incomplete bundle.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'experimental-passed-does-not-upgrade-acceptance' {
        $f = New-AcceptanceFixture 'experimental-pass'
        $declaration = New-AcceptanceBoundDeclaration $f
        $declaration.experimental = [ordered]@{
            rate=[ordered]@{status='passed';detail='Synthetic declaration only';evidenceIds=@('current-report')}
            seek=[ordered]@{status='passed';detail='Synthetic historical declaration';evidenceIds=@('historical-report')}
        }
        Write-AcceptanceJson $f.Root $f.DeclarationPath $declaration
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        $experiments = $record.sources.assessment.experimental
        Check-Acceptance ($experiments.rate.experimental -and $experiments.rate.state -ceq 'reported-pass-with-bound-evidence') 'Bound experimental declaration lost its separate classification.'
        Check-Acceptance ($experiments.seek.experimental -and $experiments.seek.state -ceq 'reported-pass-without-current-binding') 'Historical experimental pass became current evidence.'
        Check-Acceptance ($record.sources.assessment.state -ceq 'incomplete' -and 'scenarios.song-score-panel' -cin $record.sources.assessment.uncoveredItems) 'Experimental passes filled untested required scenarios.'
        $report = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Check-Acceptance ($report.reportedAssessment.state -ceq 'incomplete') 'Validation promoted experimental passes.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'current-report-and-historical-evidence-remain-classified' {
        $f = New-AcceptanceFixture 'evidence-classification'
        $declaration = New-AcceptanceBoundDeclaration $f
        $declaration.scenarios = [ordered]@{
            'song-score-panel'=[ordered]@{status='passed';evidenceIds=@('current-report')}
            'color-theme-restore'=[ordered]@{status='passed';evidenceIds=@('historical-report')}
        }
        Write-AcceptanceJson $f.Root $f.DeclarationPath $declaration
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        $roles = @($record.sources.evidenceFiles | ForEach-Object { "$($_.id)|$($_.role)" })
        Assert-AcceptanceSet $roles @('current-report|current-candidate','historical-report|historical-context','original-report|original-baseline') 'Evidence roles were inferred from synthetic passed report contents.'
        $uncovered = $record.sources.assessment.uncoveredItems
        Check-Acceptance ('scenarios.song-score-panel.current-evidence-binding' -cnotin $uncovered -and
            'scenarios.color-theme-restore.current-evidence-binding' -cin $uncovered) 'Current and historical bindings were treated equally.'
        $null = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Assert-AcceptanceSourcesStamp $f $before
        $declaration.session.kind = 'historical'
        Write-AcceptanceJson $f.Root $f.DeclarationPath $declaration
        $before = Get-AcceptanceSourcesStamp $f
        $historicalBundle = Invoke-AcceptanceCollect $f (Join-Path $f.OutputParent 'historical-session')
        $historyRecord = Assert-AcceptanceCollection $f $historicalBundle
        Check-Acceptance ('current-candidate-Standard-real-machine-session' -cin $historyRecord.sources.assessment.uncoveredItems) 'Historical session filled the current session requirement.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'complete-user-report-awaits-review-and-stays-preview' {
        $f = New-AcceptanceFixture 'complete-declaration' @(Get-PreviewBasicPluginIds) -WithInstalled
        $declaration = New-AcceptanceBoundDeclaration $f -Complete
        Write-AcceptanceJson $f.Root $f.DeclarationPath $declaration
        Write-PreviewInputFile $f.Root (Join-Path $f.Game 'BepInEx/RizToolbox/runtime.json') '{"schemaVersion":1,"mode":"Standard","logLevel":"Debug","custom":"retained user preference"}'
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        $assessment = $record.sources.assessment
        Check-Acceptance ($assessment.failedItems.Count -eq 0 -and $assessment.uncoveredItems.Count -eq 0 -and
            $assessment.state -ceq 'reported-complete-awaiting-human-review') 'Complete synthetic user declaration did not retain its awaiting-review state.'
        Check-Acceptance ($assessment.provenance -ceq 'user-declaration-and-read-only-file-snapshots') 'Declared observations became program-verified facts.'
        $config = @($record.sources.installed.ownershipManifest.files | Where-Object { $_.relativePath -ceq 'BepInEx/RizToolbox/runtime.json' })
        Check-Acceptance ($config.Count -eq 1 -and $config[0].status -ceq 'mismatched' -and -not $config[0].requiredForCandidate) 'Permitted user configuration drift was hidden from a complete report.'
        $report = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Check-Acceptance ($report.reportedAssessment.state -ceq 'reported-complete-awaiting-human-review') 'Validator promoted a complete user report.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'pending-ownership-is-reported-without-recovery' {
        $f = New-AcceptanceFixture 'pending-ownership' -WithInstalled
        $ownership = Read-AcceptanceJson $f.InstallationManifest
        $entry = $ownership.files[0]
        $ownership.pending = @([ordered]@{path=$entry.path;sha256=('0' * 64);previousSha256=$entry.sha256;operation='write'})
        $ownership.transaction = [ordered]@{schemaVersion=1;id=('a' * 32)}
        Write-AcceptanceJson $f.Root $f.InstallationManifest $ownership
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        Check-Acceptance ($record.sources.installed.ownershipManifest.state -ceq 'pending-transaction' -and
            'installation-ownership.pending-transaction' -cin $record.sources.assessment.failedItems) 'Pending journal was treated as a finalized install.'
        foreach ($row in $record.sources.installed.files) { Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'pending-transaction') 'Pending ownership was adopted from matching bytes.' }
        $report = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Check-Acceptance ($report.reportedAssessment.state -ceq 'failed') 'Validator promoted a pending journal.'
        Assert-AcceptanceSourcesStamp $f $before
    }

    Invoke-AcceptanceCase 'forged-ownership-stays-invalid-without-adoption' {
        $f = New-AcceptanceFixture 'forged-ownership' -WithInstalled
        $ownership = Read-AcceptanceJson $f.InstallationManifest
        $ownership.files = @($ownership.files) + @([ordered]@{path='GameAssembly.dll';sha256=(Get-AcceptanceHash (Join-Path $f.Game 'GameAssembly.dll'))})
        Write-AcceptanceJson $f.Root $f.InstallationManifest $ownership
        $before = Get-AcceptanceSourcesStamp $f
        $bundle = Invoke-AcceptanceCollect $f
        $record = Assert-AcceptanceCollection $f $bundle
        Check-Acceptance ($record.sources.installed.ownershipManifest.state -ceq 'invalid' -and $record.sources.installed.ownershipManifest.error -and
            'installation-ownership.invalid' -cin $record.sources.assessment.failedItems) 'Forged game ownership was accepted.'
        foreach ($row in $record.sources.installed.files) { Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'invalid') 'Invalid ownership index adopted matching payload files.' }
        $null = Assert-AcceptanceValidation $bundle (Invoke-AcceptanceValidate $f $bundle)
        Assert-AcceptanceSourcesStamp $f $before
        Remove-PreviewFixturePath $f.Root $f.InstallationManifest
        $before = Get-AcceptanceSourcesStamp $f
        $missingBundle = Invoke-AcceptanceCollect $f (Join-Path $f.OutputParent 'missing-ownership')
        $missingRecord = Assert-AcceptanceCollection $f $missingBundle
        Check-Acceptance ($missingRecord.sources.installed.ownershipManifest.state -ceq 'missing' -and
            $missingRecord.sources.installed.ownershipManifest.identity -eq $null -and
            'installation-ownership.manifest-missing' -cin $missingRecord.sources.assessment.uncoveredItems) 'Absent ownership manifest was invented.'
        foreach ($row in $missingRecord.sources.installed.files) { Check-Acceptance ($row.status -ceq 'matched' -and $row.ownership -ceq 'missing') 'Missing ownership was silently adopted.' }
        Assert-AcceptanceSourcesStamp $f $before
    }

    if ($results.Count -eq 0) { throw "No preview acceptance cases matched '$Case'." }
    foreach ($identity in @($sourceIdentities) + @($testIdentities)) {
        Check-Acceptance ((Get-AcceptanceHash $identity.path) -ceq $identity.sha256) "Test subject or helper changed during the run: $($identity.path)"
    }
} catch {
    $results.Add([ordered]@{name='test-infrastructure';passed=$false;error=$_.Exception.Message;stack=$_.ScriptStackTrace})
    Write-Output "FAIL test-infrastructure : $($_.Exception.Message)"
}

$passed = @($results | Where-Object { $_.passed }).Count
$failed = $results.Count - $passed
$cleanup = [ordered]@{path=$fixtureRoot;removed=$false}
if ($failed -eq 0 -and -not $KeepFixtures) {
    try { Remove-PreviewFixturePath $evidenceRoot $fixtureRoot; $cleanup.removed = $true }
    catch {
        $results.Add([ordered]@{name='fixture-cleanup';passed=$false;error=$_.Exception.Message;stack=$_.ScriptStackTrace})
        $cleanup.error = $_.Exception.Message
        $failed++
        Write-Output "FAIL fixture-cleanup : $($_.Exception.Message)"
    }
} else { $cleanup.reason = $(if ($failed -gt 0) { 'Preserved fixtures after failure.' } else { 'KeepFixtures requested.' }) }
$report = [ordered]@{
    schemaVersion=1;test='PreviewAcceptance';distributionSchemaVersion=3;runId=$runId
    scope='Synthetic generated packages, declarations, evidence, ownership maps and installed byte copies only.'
    repositoryRoot=$repository;startedUtc=$startedAt.ToString('o');finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
    sourceIdentities=@($sourceIdentities);testIdentities=@($testIdentities);caseFilter=$Case;fixtureRoot=$fixtureRoot
    fixtureNames=@($fixtures | ForEach-Object { $_.Name });nativeExecuted=$false;gameLaunched=$false;realGameAccess=$false;installerExecuted=$false;networkAccess=$false
    count=$results.Count;passed=$passed;failed=$failed;results=$results.ToArray();cleanup=$cleanup
}
Write-AcceptanceJson $evidenceRoot $resultPath $report
Write-Output "$passed/$($results.Count) offline preview acceptance checks passed. Report: $resultPath"
if ($failed -gt 0) { exit 1 }
