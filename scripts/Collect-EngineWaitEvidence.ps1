#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KernelDirectory,
    [Parameter(Mandatory)][string]$NativeBinaryDirectory,
    [Parameter(Mandatory)][string]$NativeCasesDirectory,
    [Parameter(Mandatory)][string]$ManagedDirectory,
    [Parameter(Mandatory)][string]$SchedulerBinaryDirectory,
    [Parameter(Mandatory)][string]$SchedulerCasesDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$JointDirectory,
    [string]$SchedulerJointDirectory,
    [string[]]$FailedNativeDirectories = @()
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "Absolute path required: $Path" }
    [IO.Path]::GetFullPath($Path)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Json([string]$Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100 }
function WriteJson([object]$Value, [string]$Path) {
    $content = $Value | ConvertTo-Json -Depth 40
    $temporary = $Path + '.atomic.tmp'
    [IO.File]::WriteAllText($temporary, $content, [Text.UTF8Encoding]::new($false))
    if ([IO.File]::ReadAllText($temporary) -cne $content) { throw 'Temporary JSON verification failed.' }
    [IO.File]::Move($temporary, $Path, $true)
}
function Require([bool]$Condition, [string]$Message) {
    $script:result.checks++
    if (-not $Condition) { throw $Message }
}
function Within([string]$Root, [string]$Path) {
    $relative = [IO.Path]::GetRelativePath($Root, $Path)
    if ($relative -eq '..' -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::IsPathFullyQualified($relative)) { throw "Evidence path escapes its declared directory: $Path" }
    $relative.Replace('\','/')
}
$directories = [ordered]@{
    kernel = Absolute $KernelDirectory; nativeBuild = Absolute $NativeBinaryDirectory
    nativeCases = Absolute $NativeCasesDirectory; managed = Absolute $ManagedDirectory
    schedulerBuild = Absolute $SchedulerBinaryDirectory; schedulerCases = Absolute $SchedulerCasesDirectory
}
if ($JointDirectory) { $directories.joint = Absolute $JointDirectory }
if ($SchedulerJointDirectory) { $directories.schedulerJoint = Absolute $SchedulerJointDirectory }
$output = Absolute $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
foreach ($directory in $directories.Values) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing evidence directory: $directory" }
    $null = Within ([IO.Path]::GetPathRoot($directory)) $directory
    $relativeOutput = [IO.Path]::GetRelativePath($directory, $output)
    if (-not [IO.Path]::IsPathFullyQualified($relativeOutput) -and
        $relativeOutput -ne '..' -and -not $relativeOutput.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)) {
        throw 'OutputDirectory must be outside all immutable input directories.'
    }
}
$null = New-Item -ItemType Directory -Path $output
$artifactRoot = Join-Path $output 'artifacts'
$null = New-Item -ItemType Directory -Path $artifactRoot
$resultPath = Join-Path $output 'wait-evidence.json'
$result = [ordered]@{
    schemaVersion = 1; kind = 'engine-wait-evidence-collection'
    startedUtc = [DateTime]::UtcNow.ToString('o'); passed = $false; availableEvidenceVerified = $false
    fullBatchVerified = $false; actualNativeWaitClrIntegration = $false; jointStatus = 'missing'
    checks = 0; directories = $directories; summaries = [ordered]@{}; artifacts = @()
    sourceFreeze = $null; managedSourceFreeze = $null; preservedFailedRuns = @(); failure = $null
    collectorExecutedFixtures = $false; gameRuntimeVerified = $false; installed = $false
    productionPatchInstallationVerified = $false; productionAdmissionVerified = $false
    allWritersProven = $false; allDispatchersProven = $false; futureSourcesClosed = $false
    engineCancellationCompleted = $false
    collector = [ordered]@{ path = $PSCommandPath; sha256 = Hash $PSCommandPath }
}
$sourceRows = [Collections.Generic.List[object]]::new()
$copied = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
function CopyEvidence([string]$Path, [string]$Label, [string]$Root, [string]$ExpectedHash = '') {
    $full = Absolute $Path
    Require (Test-Path -LiteralPath $full -PathType Leaf) "Missing original evidence: $full"
    $relative = Within $Root $full
    $key = $Label + '/' + $relative
    if ($copied.ContainsKey($key)) {
        $existing = $copied[$key]
        Require ((-not $ExpectedHash) -or $existing.sha256 -eq $ExpectedHash) "Conflicting hash for $full"
        return $existing
    }
    $stream = [IO.File]::Open($full, 'Open', 'Read', 'Read')
    try {
        $sha = Hash $full
        Require ((-not $ExpectedHash) -or $sha -eq $ExpectedHash) "Original hash mismatch: $full"
        $destination = Join-Path $artifactRoot $key
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::Copy($full, $destination, $false)
        Require ((Hash $destination) -eq $sha -and (Hash $full) -eq $sha) "Evidence changed while copying: $full"
        $record = [ordered]@{ originalPath = $full; path = [IO.Path]::GetRelativePath($output,$destination).Replace('\','/')
            bytes = $stream.Length; sha256 = $sha }
        $copied.Add($key,$record); $script:result.artifacts += $record
        return $record
    } finally { $stream.Dispose() }
}
function ReadRecorded([string]$Path, [string]$Label, [string]$Root) {
    $record = CopyEvidence $Path $Label $Root
    Json (Join-Path $output $record.path)
}
function Manifest([string]$Root, [string]$File, [string]$Kind, [string]$Label) {
    $manifest = ReadRecorded (Join-Path $Root $File) $Label $Root
    Require ($manifest.schemaVersion -eq 1 -and $manifest.kind -eq $Kind) "Wrong build manifest: $File"
    if ($Kind -eq 'engine-node-kernel-build') {
        Require ($manifest.sourceHashesUnchanged -eq $true -and $manifest.waitObservationAbi -eq 1 -and
            $manifest.waitTypedChannels -eq 3 -and $manifest.waitPhysicalKinds -eq 13 -and $manifest.waitPhaseSites -eq 18) 'Kernel Wait build contract missing.'
    } else {
        Require ($manifest.buildSucceeded -eq $true -and $manifest.buildExitCode -eq 0) "Build did not actually succeed: $File"
    }
    foreach ($fileRow in @($manifest.files)) {
        $record = CopyEvidence $fileRow.path $Label $Root $fileRow.sha256
        if ($null -ne $fileRow.bytes) { Require ($record.bytes -eq $fileRow.bytes) 'Build artifact length mismatch.' }
    }
    foreach ($sourceRow in @($manifest.sources)) {
        $sourceRows.Add([ordered]@{ path = Absolute $sourceRow.path; sha256 = $sourceRow.sha256; manifest = $Label + '/' + $File })
    }
    $manifest
}
function CaseRun([string]$Root, [string]$Kind, [string]$ChildKind, [int]$Count, [string]$Label, [string]$KernelHash) {
    $run = ReadRecorded (Join-Path $Root 'fixture-result.json') $Label $Root
    Require ($run.schemaVersion -eq 1 -and $run.kind -eq $Kind -and $run.passed -eq $true -and
        $run.failures -eq 0 -and @($run.cases).Count -eq $Count) "Incomplete actual case run: $Label"
    $null = CopyEvidence $run.runner.path ($Label + '-runner') ([IO.Path]::GetDirectoryName($run.runner.path)) $run.runner.sha256
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sum = 0L; $nativeSum = 0L
    $runKernelRows = @($run.inputs | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'RizToolbox.EngineNodeKernel.dll' })
    Require ($runKernelRows.Count -eq 1 -and $runKernelRows[0].sha256 -eq $KernelHash) 'Case run must identify the exact authoritative Kernel.'
    foreach ($inputRow in @($run.inputs)) {
        $null = CopyEvidence $inputRow.path ($Label + '-inputs') ([IO.Path]::GetDirectoryName($inputRow.path)) $inputRow.sha256
        if ($inputRow.name -eq 'RizToolbox.EngineNodeKernel.dll') { Require ($inputRow.sha256 -eq $KernelHash) 'Case Kernel differs from authoritative build.' }
    }
    $buildRecord = $null
    if ($Label -eq 'scheduler-joint') {
        $nativeRoot = Absolute $run.binaryDirectory
        $null = Manifest $nativeRoot 'scheduler-managed-fixture-build.json' 'engine-scheduler-managed-fixture-build' 'scheduler-joint-build'
    } else {
        $buildRecord = CopyEvidence $run.buildManifest.path ($Label + '-manifest') ([IO.Path]::GetDirectoryName($run.buildManifest.path)) $run.buildManifest.sha256
    }
    if ($Label -eq 'joint') {
        $jointBuild = Json (Join-Path $output $buildRecord.path)
        Require ($jointBuild.schemaVersion -eq 1 -and $jointBuild.kind -eq 'engine-wait-managed-fixture-build' -and
            $jointBuild.buildSucceeded -eq $true -and $jointBuild.buildExitCode -eq 0) 'Joint native build did not actually pass.'
        $jointBuildRoot = [IO.Path]::GetDirectoryName((Absolute $run.buildManifest.path))
        foreach ($fileRow in @($jointBuild.files)) {
            $record = CopyEvidence $fileRow.path 'joint-build' $jointBuildRoot $fileRow.sha256
            Require ($record.bytes -eq $fileRow.bytes) 'Joint native artifact length differs.'
        }
        foreach ($sourceRow in @($jointBuild.sources)) {
            $sourceRows.Add([ordered]@{ path = Absolute $sourceRow.path; sha256 = $sourceRow.sha256; manifest = 'joint-build' })
        }
        $jointAssemblies = @($run.inputs | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'RizToolbox.AudioContent.Tests.dll' })
        $checkedAssemblies = @($managed.artifacts | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'RizToolbox.AudioContent.Tests.dll' })
        Require ($jointAssemblies.Count -eq 1 -and $checkedAssemblies.Count -eq 1 -and
            $jointAssemblies[0].sha256 -eq $checkedAssemblies[0].sha256) 'Joint CLR assembly differs from the managed source freeze/build checks.'
    }
    foreach ($caseRef in @($run.cases)) {
        Require ($names.Add([string]$caseRef.name)) 'Duplicate case name.'
        $caseRecord = CopyEvidence $caseRef.path $Label $Root $caseRef.sha256
        $case = Json (Join-Path $output $caseRecord.path)
        Require ($case.name -eq $caseRef.name -and $case.passed -eq $true -and $case.started -eq $true -and
            $case.pid -gt 0 -and $case.exitCode -eq 0 -and $case.timedOut -eq $false -and @($case.issues).Count -eq 0) "Actual child did not pass: $($caseRef.name)"
        $null = CopyEvidence $case.stdout $Label $Root $case.stdoutSha256
        $null = CopyEvidence $case.stderr $Label $Root $case.stderrSha256
        $childRecord = CopyEvidence $case.childJson.path $Label $Root $case.childJson.sha256
        $child = Json (Join-Path $output $childRecord.path)
        Require ($child.schemaVersion -eq 1 -and $child.kind -eq $ChildKind -and $child.mode -eq $case.name -and
            $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0) 'Child JSON is not actual matching success evidence.'
        if ($Label -eq 'joint') {
            Require ($child.actualNativeWaitClrIntegration -eq $true -and $child.nativeReturned -eq $true -and
                $child.drained -eq $true -and $child.nativeChecks -gt 0 -and $child.nativeFailures -eq 0 -and
                $child.actualNativeToClrCallbacksVerified -eq $true -and $child.actualWaitDispatcherVerified -eq $true -and
                $child.actualWaitLedgerVerified -eq $true -and $child.schedulerMaskRouteConfigured -eq $true -and
                $child.actualSchedulerMaskRouteVerified -eq ($child.nativeCallbacks -gt 0) -and
                $child.nativeCallbacks -eq $child.capturedCallbacks -and
                $child.actualNodeIdentityLedgerVerified -eq $true -and $child.sourceModuleIsOwnedDll -eq $true -and
                $child.actualTypedCompleteDisposeVerified -eq $true -and $child.actualPerGenerationRetirementVerified -eq $true -and
                $child.nativeInFlight -eq 0 -and $child.ledgerOutstanding -eq 0 -and
                $child.ledgerPending -eq $false -and $child.ledgerUnknown -eq $false) 'Joint child lacks actual dispatcher/ledger/return/drain proof.'
            $expectedLateNullDispatch = if ($child.mode -eq 'replace') { 1 } else { 0 }
            Require ($child.actualLateNullDispatchVerified -eq ($child.mode -eq 'replace') -and
                $child.lateNullDispatchCaptures -eq $expectedLateNullDispatch -and
                $child.lateNullDispatchCompletes -eq $expectedLateNullDispatch -and
                $child.lateNullDispatchDisposes -eq $expectedLateNullDispatch) 'Exact late callback-null Dispatch was omitted, repeated, or falsely claimed.'
            if ($child.mode -eq 'replace') {
                Require ($child.nativeDispatches -eq 3) 'Replace must execute old Dispatch, exact late callback-null old Dispatch, and new Dispatch.'
            }
            Require ($child.engineCancelled -eq $false -and $child.futureProducerClosureVerified -eq $false -and
                $child.schedulerClosureVerified -eq $false -and $child.productionAdmissionVerified -eq $false -and
                $child.gameExecuted -eq $false -and $child.installed -eq $false -and
                $child.engineCancellationCompleted -eq $false -and $child.productionPublishedAdoptionUsed -eq $false -and
                $child.ledgerEngineCancelled -eq $false -and $child.ledgerFutureClosed -eq $false -and
                $child.actualTrackerBackendExecuted -eq $false) 'Wait joint overstates production or global closure.'
            $nativeSum += [long]$child.nativeChecks
        } elseif ($Label -eq 'scheduler-joint') {
            Require ($child.actualNativeToClrCallbacksVerified -eq $true -and $child.actualSchedulerDispatcherVerified -eq $true -and
                $child.actualSchedulerRowLedgerVerified -eq $true -and $child.actualNodeIdentityLedgerVerified -eq $true -and
                $child.sourceModuleIsOwnedDll -eq $true -and $child.actualCallerProfilesVerified -eq $true -and
                $child.allRowsRetiredAfterRootDisposeVerified -eq $true -and $child.actualNodeRetirementGateVerified -eq $true -and
                $child.nativeChecks -gt 0 -and $child.nativeFailures -eq 0 -and $child.nativeInFlight -eq 0 -and
                $child.dispatcherInFlight -eq 0 -and $child.ledgerOutstanding -eq 0 -and $child.ledgerPending -eq $false) 'N scheduler joint regression lacks actual source/row/drain proof.'
            Require ($child.gameExecuted -eq $false -and $child.installed -eq $false -and $child.engineCancelled -eq $false -and
                $child.productionAdmissionVerified -eq $false -and $child.engineCancellationCompleted -eq $false -and
                $child.futureProducerClosureVerified -eq $false -and $child.schedulerClosureVerified -eq $false) 'Scheduler joint overstates production or closure.'
            $nativeSum += [long]$child.nativeChecks
        } else {
            Require ($child.gameRuntimeVerified -eq $false -and $child.productionAdmissionVerified -eq $false -and
                $child.engineCancellationCompleted -eq $false) 'Child overstates production or cancellation coverage.'
            $null = CopyEvidence $case.raw.path $Label $Root $case.raw.sha256
            $raw = [IO.File]::ReadAllBytes($case.raw.path)
            Require ($raw.Length -ge 16 -and $raw.Length -eq $case.raw.bytes -and $raw.Length -eq $child.rawBytes -and
                [BitConverter]::ToUInt32($raw,0) -eq 1 -and [BitConverter]::ToUInt32($raw,4) -eq $raw.Length) 'Raw header/length differs.'
            if ($Label -eq 'native') {
                Require ([BitConverter]::ToUInt32($raw,8) -eq $child.rawChecks -and $child.rawChecks -gt 0 -and
                    [BitConverter]::ToUInt32($raw,12) -eq 0 -and $child.rawFailures -eq 0 -and
                    $child.persistenceChecks -eq 3 -and [long]$child.checks -eq [long]$child.rawChecks + 3) 'Raw/JSON assertion totals differ.'
                Require ($child.physicalKindsConfigured -eq 13 -and $child.phaseSitesConfigured -eq 18 -and
                    $child.productionPatchInstallationVerified -eq $false -and $child.futureSourcesClosed -eq $false) 'Wait observation contract differs.'
            }
        }
        $sum += [long]$child.checks
    }
    Require ($sum -eq $run.totalChecks) 'Case assertion total mismatch.'
    if ($Label -eq 'joint' -or $Label -eq 'scheduler-joint') {
        Require ($nativeSum -eq $run.totalNativeChecks) 'Joint native assertion total mismatch.'
    }
    [ordered]@{ passed = $true; cases = $Count; checks = $sum; nativeChecks = $nativeSum; names = @($names) }
}
function ManagedFreeze([object]$Managed, [string]$Root, [string]$Label) {
    $archiveRecord = CopyEvidence $Managed.inputArchive.path $Label $Root $Managed.inputArchive.sha256
    $zip = [IO.Compression.ZipFile]::OpenRead($Managed.inputArchive.path)
    try {
        Require ($zip.Entries.Count -eq $Managed.inputArchive.entries -and $zip.Entries.Count -eq @($Managed.inputs).Count) 'Managed source freeze count differs.'
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($inputRow in @($Managed.inputs)) {
            $relative = [IO.Path]::GetRelativePath($repo,$inputRow.path).Replace('\','/')
            $entryName = if ($relative.StartsWith('../')) { 'external/' + [IO.Path]::GetFileName($inputRow.path) } else { 'repo/' + $relative }
            Require ($seen.Add($entryName)) 'Duplicate managed archive source identity.'
            $entry = $zip.GetEntry($entryName)
            Require ($null -ne $entry) "Frozen source missing from archive: $entryName"
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() }
            finally { $sha.Dispose(); $stream.Dispose() }
            Require ($actual -eq $inputRow.sha256) "Managed archived source hash differs: $entryName"
        }
    } finally { $zip.Dispose() }
    [ordered]@{ artifact = $archiveRecord; entries = $Managed.inputArchive.entries; eachEntrySha256Verified = $true }
}
try {
    WriteJson $result $resultPath
    $null = CopyEvidence $PSCommandPath 'collector' $PSScriptRoot $result.collector.sha256
    $kernel = Manifest $directories.kernel 'kernel-manifest.json' 'engine-node-kernel-build' 'kernel'
    $kernelRows = @($kernel.files | Where-Object name -eq 'RizToolbox.EngineNodeKernel.dll')
    Require ($kernelRows.Count -eq 1) 'Kernel manifest must contain one Kernel.'
    $kernelHash = $kernelRows[0].sha256
    $pe = ReadRecorded (Join-Path $directories.kernel 'wait-pe-verification-r1.json') 'kernel' $directories.kernel
    Require ($pe.schemaVersion -eq 1 -and $pe.kind -eq 'engine-wait-kernel-disk-verification' -and
        $pe.passed -eq $true -and $pe.checks -gt 0 -and $pe.diskOnly -eq $true -and
        $pe.dllLoaded -eq $false -and $pe.gameRuntimeVerified -eq $false -and
        $pe.productionAdmissionVerified -eq $false -and $pe.cancellationVerified -eq $false) 'PE evidence is incomplete.'
    Require ((Hash (Join-Path $directories.kernel 'kernel-manifest.json')) -eq $pe.manifestSha256) 'PE verified a different manifest.'
    foreach ($inputRow in @($pe.inputs)) {
        $relativeInput = [IO.Path]::GetRelativePath($directories.kernel,$inputRow.path)
        $root = if ([IO.Path]::IsPathFullyQualified($relativeInput) -or $relativeInput -eq '..' -or
            $relativeInput.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)) { [IO.Path]::GetDirectoryName($inputRow.path) } else { $directories.kernel }
        $null = CopyEvidence $inputRow.path 'pe-inputs' $root $inputRow.sha256
    }
    $null = CopyEvidence $pe.verifierPath 'pe-verifier' ([IO.Path]::GetDirectoryName($pe.verifierPath)) $pe.verifierSha256
    $result.summaries.kernel = [ordered]@{ sha256 = $kernelHash; diskChecks = $pe.checks; nativeWaitAbi = 1 }
    $null = Manifest $directories.nativeBuild 'wait-fixture-build.json' 'engine-wait-fixture-build' 'native-build'
    $result.summaries.native = CaseRun $directories.nativeCases 'engine-wait-fixture-cases' 'native-engine-wait-fixture' 22 'native' $kernelHash
    $null = Manifest $directories.schedulerBuild 'scheduler-fixture-build.json' 'engine-scheduler-fixture-build' 'scheduler-build'
    $result.summaries.schedulerRegression = CaseRun $directories.schedulerCases 'engine-scheduler-fixture-cases' 'native-engine-scheduler-fixture' 16 'scheduler' $kernelHash
    $managed = ReadRecorded (Join-Path $directories.managed 'test-result.json') 'managed' $directories.managed
    Require ($managed.schemaVersion -eq 1 -and $managed.kind -eq 'engine-wait-managed-checks' -and
        $managed.passed -eq $true -and $managed.sourceHashesUnchanged -eq $true -and $managed.audioChecks -gt 0 -and
        $managed.coordinatorChecks -gt 0 -and $managed.receiverChecks -gt 0 -and
        $managed.actualNativeWaitClrIntegration -eq $false) 'Managed checks must not be mistaken for joint integration.'
    foreach ($artifact in @($managed.artifacts)) { $null = CopyEvidence $artifact.path 'managed' $directories.managed $artifact.sha256 }
    $result.managedSourceFreeze = ManagedFreeze $managed $directories.managed 'managed'
    $result.summaries.managed = [ordered]@{ passed = $true; audioChecks = $managed.audioChecks
        coordinatorChecks = $managed.coordinatorChecks; receiverChecks = $managed.receiverChecks; actualNativeWaitClrIntegration = $false }

    if ($SchedulerJointDirectory) {
        $schedulerJointPath = Join-Path $directories.schedulerJoint 'fixture-result.json'
        $schedulerJoint = Json $schedulerJointPath
        Require (@($schedulerJoint.cases).Count -gt 0) 'Scheduler joint regression has no actual children.'
        $regressionManagedRoot = [IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName((Absolute $schedulerJoint.managedDirectory)))
        $regressionManaged = ReadRecorded (Join-Path $regressionManagedRoot 'test-result.json') 'scheduler-joint-managed' $regressionManagedRoot
        Require ($regressionManaged.kind -eq 'engine-wait-managed-checks' -and $regressionManaged.passed -eq $true -and
            $regressionManaged.sourceHashesUnchanged -eq $true) 'Scheduler joint regression has no matching immutable managed build checks.'
        $regressionAssembly = @($schedulerJoint.inputs | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'RizToolbox.AudioContent.Tests.dll' })
        $regressionChecked = @($regressionManaged.artifacts | Where-Object { [IO.Path]::GetFileName($_.path) -eq 'RizToolbox.AudioContent.Tests.dll' })
        Require ($regressionAssembly.Count -eq 1 -and $regressionChecked.Count -eq 1 -and
            $regressionAssembly[0].sha256 -eq $regressionChecked[0].sha256) 'Scheduler joint CLR assembly differs from its own managed freeze.'
        $result.summaries.schedulerJointRegression = CaseRun $directories.schedulerJoint 'engine-scheduler-managed-fixture-cases' 'native-engine-scheduler-managed-integration' (@($schedulerJoint.cases).Count) 'scheduler-joint' $kernelHash
        $result.summaries.schedulerJointRegression.managedSourceFreeze = ManagedFreeze $regressionManaged $regressionManagedRoot 'scheduler-joint-managed'
    }

    if ($JointDirectory) {
        $jointPath = Join-Path $directories.joint 'fixture-result.json'
        Require (Test-Path -LiteralPath $jointPath -PathType Leaf) 'JointDirectory has no actual result.'
        $joint = Json $jointPath
        Require ($joint.kind -eq 'engine-wait-managed-fixture-cases' -and $joint.actualNativeWaitClrIntegration -eq $true -and
            @($joint.cases).Count -gt 0) 'Joint result lacks explicit actual Native-to-CLR integration.'
        $expectedModes = @('normal','owner-null','high-eax','start-clear','batch-null','callback-clear','replace','republish')
        $actualModes = @($joint.cases | ForEach-Object name)
        Require ($actualModes.Count -eq $expectedModes.Count -and
            @(Compare-Object $expectedModes ($actualModes | Sort-Object -Unique)).Count -eq 0) 'Wait joint does not cover the exact eight required modes.'
        $result.summaries.joint = CaseRun $directories.joint 'engine-wait-managed-fixture-cases' 'native-engine-wait-managed-integration' (@($joint.cases).Count) 'joint' $kernelHash
        $result.actualNativeWaitClrIntegration = $true; $result.jointStatus = 'verified'
        $result.fullBatchVerified = $true; $result.passed = $true
    } else {
        $result.jointStatus = 'missing'
        $result.summaries.joint = [ordered]@{ passed = $false; actualNativeWaitClrIntegration = $false; reason = 'No actual Native-to-CLR Wait joint result was supplied.' }
    }

    # Freeze exact manifest bytes. A later live-source edit cannot silently be
    # substituted for any build source; conflicting historical hashes stay distinct.
    $sourceManifest = [Collections.Generic.List[object]]::new()
    $sourceSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $nativeArchivePath = Join-Path $output 'native-build-sources.zip'
    $nativeZip = [IO.Compression.ZipFile]::Open($nativeArchivePath,'Create')
    try {
        foreach ($sourceRow in $sourceRows) {
            $key = $sourceRow.path + ':' + $sourceRow.sha256
            if (-not $sourceSeen.Add($key)) { continue }
            Require ((Hash $sourceRow.path) -eq $sourceRow.sha256) "Live native source drifted; cannot recreate its exact freeze: $($sourceRow.path)"
            $entryName = 'sha256/' + $sourceRow.sha256 + '/' + [IO.Path]::GetFileName($sourceRow.path)
            $entry = $nativeZip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $target = $entry.Open(); $source = [IO.File]::Open($sourceRow.path,'Open','Read','Read')
            try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
            Require ((Hash $sourceRow.path) -eq $sourceRow.sha256) 'Source changed while freezing.'
            $sourceManifest.Add([ordered]@{ originalPath = $sourceRow.path; sha256 = $sourceRow.sha256; zipEntry = $entryName; manifest = $sourceRow.manifest })
        }
    } finally { $nativeZip.Dispose() }
    $readZip = [IO.Compression.ZipFile]::OpenRead($nativeArchivePath)
    try {
        foreach ($sourceRow in $sourceManifest) {
            $entry = $readZip.GetEntry($sourceRow.zipEntry); Require ($null -ne $entry) 'New freeze entry missing.'
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() }
            finally { $sha.Dispose(); $stream.Dispose() }
            Require ($actual -eq $sourceRow.sha256) 'New archive source hash differs.'
        }
    } finally { $readZip.Dispose() }
    $result.sourceFreeze = [ordered]@{ path = 'native-build-sources.zip'; sha256 = Hash $nativeArchivePath
        entries = $sourceManifest.Count; sources = $sourceManifest.ToArray(); eachEntrySha256Verified = $true }
    foreach ($failedDirectory in $FailedNativeDirectories) {
        $failedRoot = Absolute $failedDirectory
        $failed = Json (Join-Path $failedRoot 'fixture-result.json')
        Require ($failed.kind -eq 'engine-wait-fixture-cases' -and $failed.passed -eq $false) 'Historical failure input is not a failed Wait run.'
        $label = 'failed-' + $result.preservedFailedRuns.Count
        foreach ($file in Get-ChildItem -LiteralPath $failedRoot -File -Recurse) { $null = CopyEvidence $file.FullName $label $failedRoot }
        $result.preservedFailedRuns += [ordered]@{ originalDirectory = $failedRoot; cases = @($failed.cases).Count; failures = $failed.failures; passed = $false }
    }
    $result.availableEvidenceVerified = $true

} catch {
    $result.passed = $false; $result.fullBatchVerified = $false; $result.availableEvidenceVerified = $false
    $result.failure = $_.Exception.Message
    throw
} finally {
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $result $resultPath
}
Write-Output "Wait evidence: $resultPath; availableEvidenceVerified=$($result.availableEvidenceVerified); joint=$($result.jointStatus); fullBatchVerified=$($result.fullBatchVerified)"
