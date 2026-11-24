#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameDirectory='D:/SteamLibrary/steamapps/common/Rizline',
    [string]$BasePackageDirectory,[string]$BaseManifestPath,
    [string]$Python = '',
    [string]$Pwsh='C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
. (Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1')
$repo=Split-Path -Parent $PSScriptRoot
$candidate=Get-EngineTransportProductionCandidatePackage $CandidateDirectory
$expectedPayloadCount=$candidate.Payload.Count
$sourceGame=Assert-EngineOrdinaryPath $GameDirectory -Directory
$output=Assert-EngineOrdinaryPath $OutputDirectory -MayNotExist
Assert-EnginePackage (-not (Test-Path -LiteralPath $output)) 'a fresh synthetic result directory is required.'
Assert-EnginePackage ($output -ine $sourceGame -and -not $output.StartsWith($sourceGame+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
    -not $sourceGame.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'result directory overlaps the real game.'
Assert-EnginePackage ($output -ine $candidate.Root -and -not $output.StartsWith($candidate.Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
    -not $candidate.Root.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'result directory overlaps the frozen candidate.'
if(-not $BasePackageDirectory){$BasePackageDirectory=Join-Path $repo 'artifacts/package'}
if(-not $BaseManifestPath){$BaseManifestPath=Join-Path $repo 'artifacts/package-manifest.json'}
$baseRoot=Assert-EngineOrdinaryPath $BasePackageDirectory -Directory
Assert-EnginePackage ($output -ine $baseRoot -and -not $output.StartsWith($baseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -and
    -not $baseRoot.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'result directory overlaps the frozen base package.'
$BaseManifestPath=Assert-EngineOrdinaryPath $BaseManifestPath
$baseManifest=Read-EnginePackageJson $BaseManifestPath
# This fixture deliberately starts with the historical distribution, which lacks both playback helpers.
$playbackHelpers = @(
    'BepInEx/plugins/RizToolbox/RizToolbox.SingleReturn.Prototype.dll',
    'BepInEx/plugins/RizToolbox/RizToolbox.PlaybackSourcePrototype.dll'
)
$basePaths=@(Get-EngineTransportBasePackagePaths | Where-Object { $_ -cnotin $playbackHelpers })
Assert-EnginePackage ($baseManifest.schemaVersion -eq 2) 'an actual complete Schema 2 base package is required.'
Assert-EnginePackageTree $baseRoot $basePaths;$null=Assert-EngineFileRows $baseManifest.files $basePaths $baseRoot
$null=New-Item -ItemType Directory -Path $output
$fixtureRoot=Join-Path $output 'fixtures';$null=New-Item -ItemType Directory -Path $fixtureRoot
$results=[Collections.Generic.List[object]]::new();$workers=[Collections.Generic.List[object]]::new()
$script:CommitFault=$false
$script:OriginalWriteInstallManifest=${function:Write-InstallManifest}

function Write-InstallManifest([string]$Path,$Manifest){
    if($script:CommitFault -and @($Manifest.pending).Count -eq 0 -and
        @($Manifest.files|Where-Object path -ceq 'RizToolbox.EngineBootstrap.dll').Count -eq 1){
        $script:CommitFault=$false
        throw 'Injected final commit manifest fault after actual payload writes/removal.'
    }
    & $script:OriginalWriteInstallManifest $Path $Manifest
}
function Check([bool]$Value,[string]$Reason){if(-not $Value){throw $Reason}}
function Inside([string]$Path){
    $full=[IO.Path]::GetFullPath($Path)
    Check ($full.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'A synthetic mutation escaped the resolved result directory.'
    return $full
}
function Text([string]$Path,[string]$Value){
    $path=Inside $Path;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
    [IO.File]::WriteAllText($path,$Value,[Text.UTF8Encoding]::new($false))
}
function Expect-Refusal([scriptblock]$Action,[string]$Reason){
    $failure=$null;try{& $Action|Out-Null}catch{$failure=$_.Exception.Message}
    Check (-not [string]::IsNullOrWhiteSpace($failure)) "Expected refusal: $Reason"
    return $failure
}
function Case([string]$Name,[scriptblock]$Action){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try{& $Action;$results.Add([ordered]@{name=$Name;passed=$true;elapsedMilliseconds=$watch.ElapsedMilliseconds});Write-Output "PASS $Name"}
    catch{$results.Add([ordered]@{name=$Name;passed=$false;elapsedMilliseconds=$watch.ElapsedMilliseconds;error=$_.Exception.ToString()});Write-Output "FAIL $Name : $($_.Exception.Message)"}
    finally{$script:CommitFault=$false}
}
function New-Fixture([string]$Name,[switch]$Bare){
    $root=Inside (Join-Path $fixtureRoot $Name);$game=Inside (Join-Path $root 'fake-game')
    $null=New-Item -ItemType Directory -Path $game
    # Read-only locked copies of real PE bytes, never a loader or process call.
    foreach($name in @('Rizline.exe','UnityPlayer.dll','GameAssembly.dll')){
        $source=Assert-EngineOrdinaryPath (Join-Path $sourceGame $name)
        $lease=[IO.File]::Open($source,'Open','Read','Read')
        try{
            $hash=Get-InstallHash $source
            $expected=@($candidate.FinalNative.images|Where-Object module -ceq $name)
            $expectedHash=if($name -ceq 'GameAssembly.dll'){'853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79'}else{
                Check ($expected.Count -eq 1) 'The actual fixed game role is missing.';$expected[0].sha256
            }
            Check ($hash -ceq $expectedHash) 'Real PE identity changed before the synthetic copy.'
            [IO.File]::Copy($source,(Join-Path $game $name),$false)
            Check ((Get-InstallHash $source) -ceq $hash -and (Get-InstallHash (Join-Path $game $name)) -ceq $hash) 'Original PE changed while copying.'
        }finally{$lease.Dispose()}
    }
    if(-not $Bare){Text (Join-Path $game 'BepInEx/RizToolbox/user-preserved.txt') 'unowned generated user data'}
    return [pscustomobject]@{Root=$root;Game=$game;Manifest=(Join-Path $game 'BepInEx/RizToolbox/installation.json')}
}
function Read-Current($Fixture){
    $null=Inside $Fixture.Game
    if(Test-Path -LiteralPath $Fixture.Manifest){
        $value=Read-InstallManifest $Fixture.Game $Fixture.Manifest
        return Restore-InstallTransaction $Fixture.Game $Fixture.Manifest $value
    }
    return [pscustomobject]@{owner='riz_toolbox';gameRoot=$Fixture.Game;installedAt=[DateTime]::UtcNow.ToString('o');files=@();pending=@()}
}
function Install-Transport($Fixture,[string]$Root=$candidate.Root){
    $null=Inside $Fixture.Game
    $lease=Enter-InstallLock $Fixture.Game
    try{
        $manifest=Read-Current $Fixture;$owned=Get-OwnedInstallFiles $manifest
        $plan=Get-EngineTransportCandidateInstallPlan $Root $Fixture.Game $owned $Python
        $sourceProof=$plan.Validation.operationPublication.tweenSources
        Check ($sourceProof.nativeProcesses -eq 13 -and $sourceProof.jointReferenceProcesses -eq 5 -and
            $sourceProof.exactArchivedInputs -eq 14 -and $sourceProof.productionCoreInputsMatched -eq 11 -and
            $sourceProof.sourceArchiveVerified -eq $true) 'The actual install plan omitted the fourteen-input thirteen-mode source/reference proof.'
        $rundown=$plan.Validation.operationPublication.tweenRetirement
        $references=$plan.Validation.operationPublication.tweenReferences
        Check ($rundown.nativeProcesses -eq 18 -and $rundown.productionCoreInputsMatched -eq 12 -and
            $rundown.sourceArchiveVerified -eq $true -and $rundown.sourceCountersControlled -eq $true -and
            $rundown.foreignOriginalInFlightDuringCommit -eq $true -and
            $references.nativeProcesses -eq 9 -and $references.productionCoreInputsMatched -eq 5 -and
            $references.sourceArchiveVerified -eq $true) 'The actual install plan omitted the retirement or standalone references execution proof.'
        $single=$plan.Validation.operationPublication.singleEvent
        Check ($plan.Validation.operationPublication.publicationVersion -eq 7 -and $plan.Validation.operationPublication.publicationBytes -eq 2528 -and
            $plan.Validation.operationPublication.expectedWrites -eq 64 -and $plan.Validation.operationPublication.helperExportCount -eq 88 -and
            $plan.Validation.operationPublication.playbackAttestation.version -eq 1 -and
            $plan.Validation.operationPublication.playbackAttestation.headerBytes -eq 120 -and
            $plan.Validation.operationPublication.playbackAttestation.rangeBytes -eq 184 -and
            $plan.Validation.operationPublication.playbackAttestation.originals -eq 28 -and
            $single.sectionBytes -eq 416 -and $single.spanCount -eq 7 -and $single.sourceBytes -eq 53 -and $single.blockBytes -eq 4096 -and
            $single.nativeMappedProcesses -eq 182 -and $single.sourceByteTamperProcesses -eq 53 -and @($single.sampledBlockOffsets).Count -eq 80 -and
            $single.blockTamperIsExhaustive -eq $false -and $single.protectionTamperProcesses -eq 8 -and $single.clrReadonlyAdoptionProcesses -eq 2 -and
            $single.partialPublication.processes -eq 7 -and $single.partialPublication.linkedProductionObjects -eq 21 -and
            $single.partialPublication.productionDLLInvoked -eq $false) 'The actual install plan omitted current Single native/CLR publication evidence.'
        Invoke-InstallTransaction $Fixture.Game $Fixture.Manifest $manifest $plan.Plan $owned $plan.RemovePaths
    }finally{$lease.Dispose()}
}
function Install-Base($Fixture){
    $null=Inside $Fixture.Game;$lease=Enter-InstallLock $Fixture.Game
    try{
        $manifest=Read-Current $Fixture;$owned=Get-OwnedInstallFiles $manifest
        $plan=@($baseManifest.files|ForEach-Object{[pscustomobject]@{source=(Get-EnginePackageFile $baseRoot $_.path);path=$_.path.Replace('/','\');sha256=$_.sha256}})
        Invoke-InstallTransaction $Fixture.Game $Fixture.Manifest $manifest $plan $owned @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')
    }finally{$lease.Dispose()}
}
function State($Fixture){
    $null=Inside $Fixture.Game;$value=Read-InstallManifest $Fixture.Game $Fixture.Manifest
    $map=@{};foreach($item in $value.files){$map[$item.path]=Get-InstallHash (Get-InstallTarget $Fixture.Game $item.path)}
    return $map
}
function Check-State($Fixture,[hashtable]$Expected){
    foreach($path in $Expected.Keys){Check ((Get-InstallHash (Get-InstallTarget $Fixture.Game $path)) -ceq $Expected[$path]) "Known-good bytes not restored: $path"}
    $value=Read-InstallManifest $Fixture.Game $Fixture.Manifest
    Check (@($value.pending).Count -eq 0 -and $value.PSObject.Properties.Name -notcontains 'transaction') 'A completed transaction still has a live journal.'
}
function Clone-Candidate([string]$Name){
    $root=Inside (Join-Path $output ('candidate-'+$Name));$null=New-Item -ItemType Directory -Path $root
    foreach($item in Get-ChildItem -LiteralPath $candidate.Root -Force){Microsoft.PowerShell.Management\Copy-Item -LiteralPath $item.FullName -Destination $root -Recurse}
    return $root
}
function Rehash-CandidateRows([string]$Root,[string[]]$Payload=@(),[string[]]$Evidence=@()){
    $null=Inside $Root
    $path=Inside (Join-Path $Root 'transport-candidate-package-manifest.json')
    $manifest=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    foreach($relative in $Payload){
        $rows=@($manifest.files|Where-Object path -ceq $relative);Check ($rows.Count -eq 1) 'Mutated payload role is not unique.'
        $file=Inside (Join-Path $Root ('package/'+$relative));$rows[0].sha256=Get-InstallHash $file;$rows[0].bytes=(Get-Item -LiteralPath $file).Length
    }
    foreach($relative in $Evidence){
        $rows=@($manifest.evidence|Where-Object path -ceq $relative);Check ($rows.Count -eq 1) 'Mutated evidence role is not unique.'
        $file=Inside (Join-Path $Root ('evidence/'+$relative));$rows[0].sha256=Get-InstallHash $file;$rows[0].bytes=(Get-Item -LiteralPath $file).Length
    }
    Text $path ($manifest|ConvertTo-Json -Depth 40)
}
function Check-ReadonlyCandidateRefusal($Fixture,[string]$Root,[string]$Reason){
    $entry=Entry-Fixture $Fixture;$before=Tree-State $Fixture
    $failure=Expect-Refusal {Validate-Entry $Fixture $entry $Root} $Reason
    Check ((Tree-State $Fixture) -ceq $before -and -not (Test-Path -LiteralPath $entry.PrepareMarker)) 'Invalid current-candidate validation changed installation bytes or prepared dependencies.'
    Check (-not (Test-Path -LiteralPath $Fixture.Manifest)) 'Invalid current-candidate validation created installation ownership.'
    return $failure
}
function Uninstall-OnlyOwned($Fixture){
    $null=Inside $Fixture.Game
    & (Join-Path $PSScriptRoot 'Uninstall.ps1') -GameDirectory $Fixture.Game|Out-Null
}
function Entry-Fixture($Fixture){
    $scripts=Inside (Join-Path $Fixture.Root 'scripts');$null=New-Item -ItemType Directory -Path $scripts
    foreach($name in @('Install-EngineTransportCandidate.ps1','Install.Common.ps1',
        'EngineTransportPackage.Common.ps1','EngineProductionPackage.Common.ps1','verify-engine-transport-production-package.py')){
        $source=Join-Path $PSScriptRoot $name;$destination=Inside (Join-Path $scripts $name)
        [IO.File]::Copy($source,$destination,$false)
        Check ((Get-InstallHash $source) -ceq (Get-InstallHash $destination)) 'The real installer entry/source copy differs.'
    }
    [IO.File]::Copy((Join-Path $repo 'dependencies.lock.json'),(Inside (Join-Path $Fixture.Root 'dependencies.lock.json')),$false)
    # An accidental fall-through cannot contact a network or touch the real
    # dependency cache. Only this dependency boundary is replaced; the actual
    # installer, plan validators and transaction source are byte-identical.
    $stub=@'
$ErrorActionPreference='Stop'
[IO.File]::WriteAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'prepare-called.marker'),'synthetic dependency boundary reached')
throw 'Synthetic dependency stop: Prepare/network is forbidden in this fixture.'
'@
    Text (Join-Path $scripts 'Prepare-Dependencies.ps1') $stub
    $lease=Enter-InstallLock $Fixture.Game;$lease.Dispose()
    return [pscustomobject]@{New=(Join-Path $scripts 'Install-EngineTransportCandidate.ps1');PrepareMarker=(Join-Path $Fixture.Root 'prepare-called.marker')}
}
function Tree-State($Fixture){
    $null=Inside $Fixture.Game
    return (@(Get-ChildItem -LiteralPath $Fixture.Game -Force -Recurse|Sort-Object FullName|ForEach-Object{
        $relative=[IO.Path]::GetRelativePath($Fixture.Game,$_.FullName)
        if($_.PSIsContainer){'directory/'+$relative}else{'file/'+$relative+'/'+(Get-InstallHash $_.FullName)}
    }) -join "`n")
}
function Validate-Entry($Fixture,$Entry,[string]$Root=$candidate.Root){
    $raw=@(& $Entry.New -GameDirectory $Fixture.Game -CandidateDirectory $Root -Python $Python -ValidateOnly)
    $value=($raw -join "`n")|ConvertFrom-Json -AsHashtable
    Check ($value.kind -ceq 'engine-transport-candidate-install-validation' -and $value.passed -eq $true -and
        $value.validateOnly -eq $true -and $value.payloadFiles -eq $expectedPayloadCount -and $value.gameAssemblyVerified -eq $true -and
        $value.finalGraphComplete -eq $true -and $value.publicationVersion -eq 5 -and $value.nativeProfile -eq 2) 'The actual ValidateOnly entry did not produce complete V5 disk validation.'
    foreach($name in @('journalRecoveryPerformed','dependenciesPrepared','payloadWritten','nativeExecuted','gameExecuted','installed')){
        Check ($value.ContainsKey($name) -and $value[$name] -is [bool] -and $value[$name] -eq $false) 'ValidateOnly must retain every mutation/execution field false.'
    }
    Check ($value.ContainsKey('productionAdmissionGranted') -and $value['productionAdmissionGranted'] -is [bool] -and $value['productionAdmissionGranted'] -eq [bool]$candidate.Manifest.productionAdmissionGranted) 'ValidateOnly must reflect package productionAdmissionGranted.'
    Check (-not (Test-Path -LiteralPath $Entry.PrepareMarker)) 'A readonly validation reached dependency preparation.'
    Text (Join-Path $Fixture.Root 'validate-only-result.json') ($value|ConvertTo-Json -Depth 8)
}
function Interrupt-Worker($Fixture,[string]$CandidateRoot){
    $marker=Inside (Join-Path $Fixture.Root 'worker-blocked.marker')
    $source=[IO.Path]::GetFullPath((Join-Path $CandidateRoot 'package/RizToolbox.EngineBootstrap.dll'))
    $worker=@'
$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
. '__COMMON__'
. '__TRANSPORT__'
function Copy-Item {
    [CmdletBinding()]param([string]$LiteralPath,[string]$Destination)
    if($LiteralPath -ceq '__STOP_SOURCE__'){
        [IO.File]::WriteAllText('__MARKER__',[string]$PID)
        while($true){Start-Sleep -Milliseconds 100}
    }
    Microsoft.PowerShell.Management\Copy-Item @PSBoundParameters
}
$game='__GAME__';$manifestPath='__MANIFEST__'
$lease=Enter-InstallLock $game
try{
    $manifest=Read-InstallManifest $game $manifestPath
    $manifest=Restore-InstallTransaction $game $manifestPath $manifest
    $owned=Get-OwnedInstallFiles $manifest
    $plan=Get-EngineTransportCandidateInstallPlan '__CANDIDATE__' $game $owned '__PYTHON__'
    Invoke-InstallTransaction $game $manifestPath $manifest $plan.Plan $owned $plan.RemovePaths
}finally{$lease.Dispose()}
'@
    foreach($pair in @(@('__COMMON__',(Join-Path $PSScriptRoot 'Install.Common.ps1')),@('__TRANSPORT__',(Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1')),
        @('__STOP_SOURCE__',$source),@('__MARKER__',$marker),@('__GAME__',$Fixture.Game),@('__MANIFEST__',$Fixture.Manifest),@('__CANDIDATE__',$CandidateRoot),@('__PYTHON__',$Python))){
        $worker=$worker.Replace($pair[0],$pair[1].Replace("'","''"))
    }
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($worker))
    $process=Start-Process -FilePath $Pwsh -ArgumentList @('-NoProfile','-EncodedCommand',$encoded) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Inside (Join-Path $Fixture.Root 'worker.stdout.log')) -RedirectStandardError (Inside (Join-Path $Fixture.Root 'worker.stderr.log'))
    $workerPid=$process.Id;$stopped=$false
    try{
        $deadline=[DateTime]::UtcNow.AddSeconds(60)
        while(-not (Test-Path -LiteralPath $marker) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 50}
        Check (Test-Path -LiteralPath $marker) 'Real worker did not reach its journaled selected payload write.'
        Check ([int][IO.File]::ReadAllText($marker) -eq $workerPid) 'Interruption marker belongs to another process.'
        Stop-Process -Id $workerPid -Force
        Check ($process.WaitForExit(10000)) 'The exact interrupted worker did not terminate.'
        $process.Refresh();Check $process.HasExited 'Interrupted worker remains live.';$stopped=$true
        $journal=Get-Content -LiteralPath $Fixture.Manifest -Raw|ConvertFrom-Json
        Check ($journal.PSObject.Properties.Name -contains 'transaction' -and @($journal.pending).Count -gt 0) 'Kill did not leave the actual persistent journal.'
        $workers.Add([ordered]@{pid=$workerPid;markedPid=[int][IO.File]::ReadAllText($marker);stopped=$true;terminalConfirmed=$process.HasExited;exitCode=$process.ExitCode;marker=$marker})
    }finally{
        if(-not $stopped -and -not $process.HasExited){Stop-Process -Id $workerPid -Force;$null=$process.WaitForExit(10000)}
        $process.Dispose()
    }
}

$inputPaths=@($PSCommandPath,(Join-Path $PSScriptRoot 'Install.Common.ps1'),(Join-Path $PSScriptRoot 'Uninstall.ps1'),
    (Join-Path $PSScriptRoot 'Install-EngineTransportCandidate.ps1'),(Join-Path $repo 'dependencies.lock.json'),
    (Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1'),(Join-Path $PSScriptRoot 'EngineProductionPackage.Common.ps1'),
    (Join-Path $PSScriptRoot 'verify-engine-transport-production-package.py'),$BaseManifestPath)+
    @($candidate.Manifest.files|ForEach-Object{Get-EnginePackageFile $candidate.PackageRoot $_.path})+
    @($candidate.Manifest.evidence|ForEach-Object{Get-EnginePackageFile (Join-Path $candidate.Root 'evidence') $_.path})+
    @((Join-Path $candidate.Root 'transport-candidate-package-manifest.json'))+
    @($baseManifest.files|ForEach-Object{Get-EnginePackageFile $baseRoot $_.path})
$inputRows=@($inputPaths|Sort-Object -Unique|ForEach-Object{@{path=$_;sha256=Get-InstallHash $_}})
$report=[ordered]@{schemaVersion=1;kind='engine-transport-candidate-synthetic-install-matrix';passed=$false;scope='Synthetic resolved game directories and disk PE validation only';
    candidateManifestSha256=Get-InstallHash (Join-Path $candidate.Root 'transport-candidate-package-manifest.json');inputs=$inputRows;inputHashesUnchanged=$false;
    realGameFilesReadOnlyCopied=@('Rizline.exe','UnityPlayer.dll','GameAssembly.dll');realGameMutated=$false;nativeExecuted=$false;gameExecuted=$false;prepareDependenciesExecuted=$false;
    productionAdmissionGranted=([bool]$candidate.Manifest.productionAdmissionGranted);installed=$false;expectedCases=26;operationPublicationVersion=7;operationPublicationBytes=2528;operationCount=26;operationExpectedWrites=64;tweenSourceCount=22;
    helperExportCount=88;playbackAttestationVersion=1;playbackAttestationHeaderBytes=120;playbackAttestationRangeBytes=184;playbackAttestationOriginalCount=28;
    singleEventSectionBytes=416;singleEventSpanCount=7;singleNativeMappedProcesses=182;singleSourceByteTamperProcesses=53;singleSampledBlockTamperProcesses=80;singleProtectionTamperProcesses=8;
    candidateEvidenceFiles=@($candidate.Manifest.evidence).Count;ownedTweenSourceProcesses=13;ownedTweenSourceInputs=14;ownedTweenSourceProductionCoreInputs=11;
    ownedTweenRetirementProcesses=18;ownedTweenReferenceProcesses=9;
    baseScope='current-base-package-ownership-and-layout-upgrade';
    cases=@();workers=@();failure=$null}
try{
    Case 'fresh-complete-payload-and-idempotent-reinstall' {
        $f=New-Fixture 'fresh';Install-Transport $f;$before=State $f;Check ($before.Count -eq $expectedPayloadCount) 'Fresh transaction did not own the complete V5 payload.'
        foreach ($path in $playbackHelpers) {
            Check ($before[$path.Replace('/', '\')] -ceq $candidate.Payload[$path]) "Playback helper was not installed from the candidate: $path"
        }
        Install-Transport $f;Check-State $f $before
        Check ([IO.File]::ReadAllText((Join-Path $f.Game 'BepInEx/RizToolbox/user-preserved.txt')) -ceq 'unowned generated user data') 'Reinstall changed unowned bytes.'
    }
    Case 'upgrade-actual-base23-nested-Kernel-to-root-V5' {
        $f=New-Fixture 'upgrade';Install-Base $f
        $nested=Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
        Check (Test-Path -LiteralPath $nested) 'Actual base23 did not install its legacy nested Kernel.'
        Install-Transport $f
        Check (-not (Test-Path -LiteralPath $nested)) 'Upgrade retained the old nested Kernel.'
        Check ((Get-InstallHash (Join-Path $f.Game 'RizToolbox.EngineNodeKernel.dll')) -ceq $candidate.Payload['RizToolbox.EngineNodeKernel.dll']) 'Root Kernel differs after upgrade.'
        $after = State $f
        $retained = @($baseManifest.files | Where-Object {
            $_.path -cne 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll' -and !$candidate.Payload.ContainsKey($_.path)
        })
        foreach ($row in $retained) {
            Check ($after[$row.path.Replace('/','\')] -ceq $row.sha256) "Previously installed content changed: $($row.path)"
        }
        Check ($after.Count -eq ($expectedPayloadCount + $retained.Count)) 'Upgrade ownership count differs.'
    }
    Case 'foreign-loader-file-rejected-without-overwrite' {
        $f=New-Fixture 'foreign';$path=Join-Path $f.Game 'winhttp.dll';Text $path 'third-party-owned loader';$hash=Get-InstallHash $path
        $null=Expect-Refusal {Install-Transport $f} 'foreign loader'
        Check ((Get-InstallHash $path) -ceq $hash -and -not (Test-Path -LiteralPath $f.Manifest)) 'Refusal changed foreign loader or wrote ownership.'
    }
    Case 'modified-installed-owned-file-refused' {
        $f=New-Fixture 'modified-installed';Install-Transport $f
        $path=Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll';Text $path 'externally modified installed owner';$hash=Get-InstallHash $path;$journal=Get-InstallHash $f.Manifest
        $null=Expect-Refusal {Install-Transport $f} 'modified owned target'
        Check ((Get-InstallHash $path) -ceq $hash -and (Get-InstallHash $f.Manifest) -ceq $journal) 'Modified-target refusal rewrote bytes or journal.'
    }
    Case 'modified-candidate-hash-refused' {
        $f=New-Fixture 'modified-package';$copy=Clone-Candidate 'modified';Text (Join-Path $copy 'package/RizToolbox.EngineBootstrap.dll') 'modified package source'
        $null=Expect-Refusal {Install-Transport $f $copy} 'modified candidate'
        Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'winhttp.dll'))) 'Invalid candidate copied a loader.'
    }
    Case 'actual-current-entry-rejects-V3-helper-even-with-rebound-package-hashes' {
        $f=New-Fixture 'entry-old-helper' -Bare;$copy=Clone-Candidate 'rejected-helper'
        $helperRelative='BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll'
        $oldHelper=Get-EnginePackageFile $baseRoot $helperRelative
        # This is a malformed-current-package negative, not an old-version
        # installation or compatibility route. Inspect the actual invalid PE;
        # it is copied as inert bytes and is never loaded or executed.
        $readerProbe=@'
import json,sys,pefile
image=pefile.PE(sys.argv[1],fast_load=True)
image.parse_data_directories(directories=[0])
names=sorted(entry.name.decode('ascii') for entry in image.DIRECTORY_ENTRY_EXPORT.symbols if entry.name and entry.name.startswith(b'RizFirstInstall_GetInfo'))
print(json.dumps(dict(readers=names,amd64=image.FILE_HEADER.Machine==0x8664)))
'@
        $probeLines=@(& $Python -B -c $readerProbe $oldHelper)
        Check ($LASTEXITCODE -eq 0) 'The actual rejected-helper PE could not be inspected.'
        $probe=($probeLines -join "`n")|ConvertFrom-Json -AsHashtable
        Check ($probe.amd64 -eq $true -and $probe.readers -ccontains 'RizFirstInstall_GetInfoV3' -and
            $probe.readers -cnotcontains 'RizFirstInstall_GetInfoV7') 'The malformed-current-package negative requires a real V3-only publication helper.'
        [IO.File]::Copy($oldHelper,(Inside (Join-Path $copy ('package/'+$helperRelative))),$true)
        $oldHash=Get-InstallHash $oldHelper;$oldBytes=(Get-Item -LiteralPath $oldHelper).Length
        $buildPath=Inside (Join-Path $copy 'evidence/operation-native-build.json')
        $build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
        $helperRows=@($build.files|Where-Object{[IO.Path]::GetFileName($_.path) -ceq 'RizToolbox.JudgeContext.dll'})
        Check ($helperRows.Count -eq 1) 'Current operation build has no unique helper output.'
        $helperRows[0].sha256=$oldHash;$helperRows[0].bytes=$oldBytes
        $exportsPath=Inside (Join-Path $copy 'evidence/operation-export-validation.json')
        $exports=Get-Content -LiteralPath $exportsPath -Raw|ConvertFrom-Json
        $exports.helper.sha256=$oldHash
        Text $exportsPath ($exports|ConvertTo-Json -Depth 30)
        $build.actualExportValidation.sha256=Get-InstallHash $exportsPath
        Text $buildPath ($build|ConvertTo-Json -Depth 30)
        $adoptionPath=Inside (Join-Path $copy 'evidence/operation-managed-proof.json')
        $adoption=Get-Content -LiteralPath $adoptionPath -Raw|ConvertFrom-Json;$adoption.helperSha256=$oldHash
        Text $adoptionPath ($adoption|ConvertTo-Json -Depth 30)
        # Keep the gate's helper-bound shallow links coherent too. The owned
        # source proof has no helper execution/hash claim: it is bound by the
        # archived nine production core bytes, which are unchanged in this probe.
        # This deliberate
        # malformed-package probe must reach actual PE/source verification,
        # rather than stop at a newly added outer evidence hash relation.
        $gateResultPath=Inside (Join-Path $copy 'evidence/tween-admission-native-result.json')
        $gateResult=Get-Content -LiteralPath $gateResultPath -Raw|ConvertFrom-Json
        $gateResult.helperSha256=$oldHash;$gateResult.nativeBuildManifestSha256=Get-InstallHash $buildPath
        Text $gateResultPath ($gateResult|ConvertTo-Json -Depth 30)
        $gatePath=Inside (Join-Path $copy 'evidence/tween-admission-native-proof.json')
        $gate=Get-Content -LiteralPath $gatePath -Raw|ConvertFrom-Json
        $gate.helper.sha256=$oldHash;$gate.nativeBuildManifest.sha256=Get-InstallHash $buildPath
        $gate.result.sha256=Get-InstallHash $gateResultPath
        Text $gatePath ($gate|ConvertTo-Json -Depth 30)
        Rehash-CandidateRows $copy @($helperRelative) @('operation-native-build.json','operation-export-validation.json','operation-managed-proof.json',
            'tween-admission-native-proof.json','tween-admission-native-result.json')
        # The ordinary package hashes and the shallow provenance links now
        # agree deliberately. Only the real PE/source/actual-evidence contract
        # can reject this substitution; do not settle for the outer hash guard.
        $null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'rebound V3 helper in the current twenty-six-operation package'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The real current helper PE validation was not reached.'
        Text (Join-Path $f.Root 'rejected-helper-result.json') (@{oldHelperSha256=$oldHash;actualPublicationReaders=$probe.readers;
            outerHashesRebound=$true;shallowPackageRelationsPassed=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-missing-operation-build-evidence-readonly' {
        $f=New-Fixture 'entry-missing-operation-proof' -Bare;$copy=Clone-Candidate 'missing-operation-proof'
        $missing=Inside (Join-Path $copy 'evidence/operation-native-build.json');Remove-Item -LiteralPath $missing
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'missing current operation provenance'
        Text (Join-Path $f.Root 'missing-proof-result.json') (@{missingEvidence='operation-native-build.json';rejected=$true;
            reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-missing-tween-source-proof-readonly' {
        $f=New-Fixture 'entry-missing-source-proof' -Bare;$copy=Clone-Candidate 'missing-source-proof'
        $missing=Inside (Join-Path $copy 'evidence/tween-source-native-proof.json');Remove-Item -LiteralPath $missing
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'missing actual owned Tween source proof'
        Check ($failure.Contains('tween-source-native-proof.json')) 'The missing source proof was not checked by the real entry.'
        Text (Join-Path $f.Root 'missing-source-proof-result.json') (@{missingEvidence='tween-source-native-proof.json';
            rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-tween-source-archive-byte-tamper-with-rebound-outer-hashes' {
        $f=New-Fixture 'entry-source-archive-tamper' -Bare;$copy=Clone-Candidate 'source-archive-tamper'
        $archivePath=Inside (Join-Path $copy 'evidence/tween-source-native-sources.zip')
        $sourceName='native/RizToolbox.JudgeContext/TweenSourceReferences.cpp'
        $archive=[IO.Compression.ZipFile]::Open($archivePath,'Update')
        try{
            $entry=$archive.GetEntry($sourceName);Check ($null -ne $entry) 'The actual source archive entry is missing.'
            $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
            try{$stream.CopyTo($memory);$changed=$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
            Check ($changed.Length -gt 0) 'The source archive negative has no actual byte to change.'
            $changed[0]=$changed[0] -bxor 1;$entry.Delete()
            $replacement=$archive.CreateEntry($sourceName,[IO.Compression.CompressionLevel]::Optimal);$destination=$replacement.Open()
            try{$destination.Write($changed,0,$changed.Length)}finally{$destination.Dispose()}
        }finally{$archive.Dispose()}
        $archiveHash=Get-InstallHash $archivePath;$archiveBytes=(Get-Item -LiteralPath $archivePath).Length
        $resultPath=Inside (Join-Path $copy 'evidence/tween-source-native-result.json')
        $sourceResult=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
        $sourceResult.sourceArchive.sha256=$archiveHash;$sourceResult.sourceArchive.bytes=$archiveBytes
        Text $resultPath ($sourceResult|ConvertTo-Json -Depth 40)
        $proofPath=Inside (Join-Path $copy 'evidence/tween-source-native-proof.json')
        $proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
        $proof.sourceArchive.sha256=$archiveHash;$proof.sourceArchive.bytes=$archiveBytes
        $sourceInputRows=@($proof.sourceFiles|Where-Object relativePath -ceq $sourceName)
        Check ($sourceInputRows.Count -eq 1) 'The reference-core negative has no exact declared fixture input.'
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$sourceInputRows[0].sha256=[Convert]::ToHexString($sha.ComputeHash($changed)).ToLowerInvariant()}finally{$sha.Dispose()}
        $proof.result.sha256=Get-InstallHash $resultPath
        Text $proofPath ($proof|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('tween-source-native-proof.json','tween-source-native-result.json','tween-source-native-sources.zip')
        # Rebind the fixture's declared input too. Its archive is self-consistent;
        # only comparison with the actual helper's independently archived nine
        # production core inputs can reject this changed new reference source.
        $null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'rebound source ZIP containing a changed independently archived reference-core byte'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The independent source archive byte check was not reached.'
        Text (Join-Path $f.Root 'source-archive-tamper-result.json') (@{changedSource=$sourceName;sourceArchiveSha256=$archiveHash;
            outerHashesRebound=$true;declaredFixtureInputRebound=$true;shallowPackageRelationsPassed=$true;
            rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-missing-tween-retirement-proof-readonly' {
        $f=New-Fixture 'entry-missing-retirement-proof' -Bare;$copy=Clone-Candidate 'missing-retirement-proof'
        Remove-Item -LiteralPath (Inside (Join-Path $copy 'evidence/tween-retirement-native-proof.json'))
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'missing actual native retirement proof'
        Text (Join-Path $f.Root 'missing-retirement-proof-result.json') (@{missingEvidence='tween-retirement-native-proof.json';
            rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-tween-retirement-core-tamper-with-rebound-outer-hashes' {
        $f=New-Fixture 'entry-retirement-core-tamper' -Bare;$copy=Clone-Candidate 'retirement-core-tamper'
        $archivePath=Inside (Join-Path $copy 'evidence/tween-retirement-native-sources.zip')
        $sourceName='native/RizToolbox.JudgeContext/TweenRetirementContext.cpp'
        $archive=[IO.Compression.ZipFile]::Open($archivePath,'Update')
        try{
            $entry=$archive.GetEntry($sourceName);Check ($null -ne $entry) 'No actual retirement core source was archived.'
            $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
            try{$stream.CopyTo($memory);$changed=$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
            Check ($changed.Length -gt 0) 'The retirement archive has no actual byte to change.'
            $changed[0]=$changed[0] -bxor 1;$entry.Delete()
            $replacement=$archive.CreateEntry($sourceName,[IO.Compression.CompressionLevel]::Optimal);$stream=$replacement.Open()
            try{$stream.Write($changed,0,$changed.Length)}finally{$stream.Dispose()}
        }finally{$archive.Dispose()}
        $proofPath=Inside (Join-Path $copy 'evidence/tween-retirement-native-proof.json')
        $proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
        $proof.sourceArchive.sha256=Get-InstallHash $archivePath
        $inputs=@($proof.sources|Where-Object relativePath -ceq $sourceName)
        Check ($inputs.Count -eq 1) 'The retirement proof has no unique actual compiled source row.'
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$inputs[0].sha256=[Convert]::ToHexString($sha.ComputeHash($changed)).ToLowerInvariant()}finally{$sha.Dispose()}
        Text $proofPath ($proof|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('tween-retirement-native-proof.json','tween-retirement-native-sources.zip')
        $null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'rebound retirement proof with a changed independently archived production core byte'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'Independent retirement source comparison was not reached.'
        Text (Join-Path $f.Root 'retirement-core-tamper-result.json') (@{changedSource=$sourceName;
            outerHashesRebound=$true;declaredFixtureInputRebound=$true;shallowPackageRelationsPassed=$true;
            rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-current-entry-rejects-operation-adoption-from-other-source-snapshot' {
        $f=New-Fixture 'entry-other-operation-source' -Bare;$copy=Clone-Candidate 'other-operation-source'
        $path=Inside (Join-Path $copy 'evidence/operation-managed-proof.json')
        $value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $prior=$value.sourceSnapshot.sha256;$value.sourceSnapshot.sha256='0'*64
        Check ($value.sourceSnapshot.sha256 -cne $prior) 'The source-proof negative did not change its actual snapshot binding.'
        Text $path ($value|ConvertTo-Json -Depth 30)
        Rehash-CandidateRows $copy @() @('operation-managed-proof.json')
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'current operation adoption bound to another compiled source snapshot'
        Check ($failure.Contains('actual V7 adoption differs from the payload helper or managed source snapshot')) 'The current helper/source-snapshot binding was not checked.'
        Text (Join-Path $f.Root 'other-source-proof-result.json') (@{originalSourceSnapshotSha256=$prior;substitutedSourceSnapshotSha256=$value.sourceSnapshot.sha256;
            outerHashRebound=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-V7-entry-rejects-missing-Single-section-with-rebound-result-hash' {
        $f=New-Fixture 'entry-missing-single-section' -Bare;$copy=Clone-Candidate 'missing-single-section'
        $path=Inside (Join-Path $copy 'evidence/operation-native-result.json');$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $value.PSObject.Properties.Remove('singleEventPublication');Text $path ($value|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('operation-native-result.json');$null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'V7 result without its actual Single section despite rebound outer hash'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The real verifier did not inspect the missing Single section.'
        Text (Join-Path $f.Root 'missing-single-section-result.json') (@{outerHashRebound=$true;shallowPackageRelationsPassed=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-V7-entry-rejects-omitted-Single-source-byte-mode-with-rebound-counts' {
        $f=New-Fixture 'entry-missing-single-mode' -Bare;$copy=Clone-Candidate 'missing-single-mode'
        $path=Inside (Join-Path $copy 'evidence/operation-native-result.json');$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $removed=@($value.tamperModes|Where-Object mode -ceq 'tamper-single-span-0-byte-0');Check ($removed.Count -eq 1) 'No unique actual source-byte mode to remove.'
        $value.tamperModes=@($value.tamperModes|Where-Object mode -cne 'tamper-single-span-0-byte-0');$value.independentProcesses--;$value.allProcessChecks-=$removed[0].checks
        Text $path ($value|ConvertTo-Json -Depth 40);Rehash-CandidateRows $copy @() @('operation-native-result.json');$null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'V7 proof with a missing actual Single byte process and internally rebound totals'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The real verifier did not inspect the exact Single mode set.'
        Text (Join-Path $f.Root 'missing-single-mode-result.json') (@{removedMode=$removed[0].mode;outerHashRebound=$true;summaryCountsRebound=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-V7-entry-rejects-CLR-Single-parent-substitution-with-rebound-proof-hash' {
        $f=New-Fixture 'entry-single-clr-parent' -Bare;$copy=Clone-Candidate 'single-clr-parent'
        $path=Inside (Join-Path $copy 'evidence/operation-managed-proof.json');$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $value.modes[0].singleEventPublication.parentEpoch++;Text $path ($value|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('operation-managed-proof.json');$null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'actual V7 CLR proof whose Single parent differs from operation row 25'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The actual CLR Single parent relation was not checked.'
        Text (Join-Path $f.Root 'single-clr-parent-result.json') (@{outerHashRebound=$true;shallowPackageRelationsPassed=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-V7-entry-rejects-CLR-Single-memory-claim-with-rebound-proof-hash' {
        $f=New-Fixture 'entry-single-clr-memory' -Bare;$copy=Clone-Candidate 'single-clr-memory'
        $path=Inside (Join-Path $copy 'evidence/operation-managed-proof.json');$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $value.modes[0].operationAdoption.SingleEventMemoryVerified=$false;Text $path ($value|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('operation-managed-proof.json');$null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'actual V7 CLR proof without independent complete Single memory verification'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The actual CLR Single full-memory requirement was not checked.'
        Text (Join-Path $f.Root 'single-clr-memory-result.json') (@{outerHashRebound=$true;shallowPackageRelationsPassed=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'actual-V7-entry-rejects-missing-linked-object-partial-proof-with-rebound-hash' {
        $f=New-Fixture 'entry-missing-single-partial' -Bare;$copy=Clone-Candidate 'missing-single-partial'
        $path=Inside (Join-Path $copy 'evidence/operation-native-result.json');$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $value.PSObject.Properties.Remove('singlePartialPublication');Text $path ($value|ConvertTo-Json -Depth 40)
        Rehash-CandidateRows $copy @() @('operation-native-result.json');$null=Get-EngineTransportProductionCandidatePackage $copy
        $failure=Check-ReadonlyCandidateRefusal $f $copy 'V7 package without actual bounded linked-object partial publication evidence'
        Check ($failure.Contains('actual relocated final PE validation failed')) 'The real verifier did not inspect linked-object partial publication evidence.'
        Text (Join-Path $f.Root 'missing-single-partial-result.json') (@{outerHashRebound=$true;shallowPackageRelationsPassed=$true;rejected=$true;reason=$failure;nativeExecuted=$false}|ConvertTo-Json -Depth 8)
    }
    Case 'path-escape-and-game-junction-refused' {
        $f=New-Fixture 'path-refusal'
        $null=Expect-Refusal {Get-InstallTarget $f.Game '..\escape.dll'} 'path escape'
        $outside=Inside (Join-Path $f.Root 'outside');Text (Join-Path $outside 'canary.txt') 'untouched junction target'
        $null=New-Item -ItemType Junction -Path (Inside (Join-Path $f.Game 'BepInEx/plugins')) -Target $outside
        $null=Expect-Refusal {Install-Transport $f} 'linked game target'
        Check ([IO.File]::ReadAllText((Join-Path $outside 'canary.txt')) -ceq 'untouched junction target') 'Linked target was changed.'
    }
    Case 'candidate-reparse-and-manifest-path-refused' {
        $f=New-Fixture 'candidate-path';$copy=Clone-Candidate 'unsafe'
        $manifestPath=Join-Path $copy 'transport-candidate-package-manifest.json';$value=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
        $value.files[0].path='../escape.dll';Text $manifestPath ($value|ConvertTo-Json -Depth 30)
        $null=Expect-Refusal {Install-Transport $f $copy} 'candidate path escape'
        $linked=Inside (Join-Path $output 'candidate-junction');$null=New-Item -ItemType Junction -Path $linked -Target $candidate.Root
        $null=Expect-Refusal {Install-Transport $f $linked} 'linked candidate root'
        Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'winhttp.dll'))) 'Unsafe candidate copied a loader.'
    }
    Case 'final-commit-fault-rolls-back-complete-prior-version' {
        $f=New-Fixture 'commit-fault';Install-Base $f;$before=State $f
        $script:CommitFault=$true;$failure=Expect-Refusal {Install-Transport $f} 'injected final commit fault'
        Check ($failure.Contains('Injected final commit manifest fault')) 'The intended final commit fault was not reached.'
        Check-State $f $before
        Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll')) -and
            -not (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineNodeKernel.dll'))) 'Rollback retained new root-layout artifacts.'
    }
    Case 'actual-killed-worker-journal-recovers-before-missing-candidate' {
        $f=New-Fixture 'hard-kill';Install-Base $f;$before=State $f;$copy=Clone-Candidate 'interruption'
        Interrupt-Worker $f $copy
        Check ((Get-InstallHash (Join-Path $f.Game 'winhttp.dll')) -cne $before['winhttp.dll']) 'Interruption occurred before any actual payload replacement.'
        $entry=Entry-Fixture $f;$interrupted=Tree-State $f;$journalHash=Get-InstallHash $f.Manifest
        $failure=Expect-Refusal {Validate-Entry $f $entry} 'ValidateOnly unfinished journal'
        Check ($failure.Contains('ValidateOnly refuses an unfinished installation journal')) 'The actual ValidateOnly entry did not reject the pending recovery boundary.'
        Check ((Tree-State $f) -ceq $interrupted -and (Get-InstallHash $f.Manifest) -ceq $journalHash -and
            -not (Test-Path -LiteralPath $entry.PrepareMarker)) 'ValidateOnly recovered or changed an unfinished journal.'
        $manifest=Inside (Join-Path $copy 'transport-candidate-package-manifest.json');Remove-Item -LiteralPath $manifest
        $null=Expect-Refusal {Install-Transport $f $copy} 'missing candidate after rollback'
        Check-State $f $before
        $null=Expect-Refusal {Install-Transport $f $copy} 'idempotent rollback without candidate'
        Check-State $f $before
    }
    Case 'uninstall-preserves-modified-and-unowned-bytes' {
        $f=New-Fixture 'uninstall';Install-Transport $f
        $modified=Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.Sdk.dll';Text $modified 'user retained modified SDK';$hash=Get-InstallHash $modified
        Uninstall-OnlyOwned $f;Uninstall-OnlyOwned $f
        Check ((Get-InstallHash $modified) -ceq $hash) 'Uninstall removed a modified owned file.'
        Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'winhttp.dll'))) 'Uninstall retained unchanged owned loader.'
        Check ([IO.File]::ReadAllText((Join-Path $f.Game 'BepInEx/RizToolbox/user-preserved.txt')) -ceq 'unowned generated user data') 'Uninstall removed unowned data.'
        Check (@((Read-InstallManifest $f.Game $f.Manifest).files).Count -eq 1) 'Uninstall ownership did not retain exactly the one modified owner.'
    }
    Case 'wrong-GameAssembly-refused-by-real-install-plan' {
        $f=New-Fixture 'wrong-gameassembly-plan';Text (Join-Path $f.Game 'GameAssembly.dll') 'incorrect original GameAssembly bytes'
        $before=Tree-State $f
        $failure=Expect-Refusal {Get-EngineTransportCandidateInstallPlan $candidate.Root $f.Game @{} $Python} 'wrong original GameAssembly'
        Check ($failure.Contains('unsupported actual GameAssembly.dll fingerprint')) 'The plan did not reject the independent exact GameAssembly identity.'
        Check ((Tree-State $f) -ceq $before) 'Unsupported-game plan changed synthetic original or payload bytes.'
    }
    Case 'actual-V5-ValidateOnly-fresh-and-reinstall-are-readonly' {
        $f=New-Fixture 'entry-validate' -Bare;$entry=Entry-Fixture $f;$before=Tree-State $f
        Validate-Entry $f $entry
        Check ((Tree-State $f) -ceq $before -and -not (Test-Path -LiteralPath $f.Manifest)) 'Fresh ValidateOnly wrote an installation manifest or payload.'
        Install-Transport $f;$before=Tree-State $f
        Validate-Entry $f $entry
        Check ((Tree-State $f) -ceq $before) 'Existing V5 ValidateOnly changed ownership, original or payload bytes.'
    }
    Case 'actual-V5-ValidateOnly-wrong-GameAssembly-refuses-readonly' {
        $f=New-Fixture 'entry-wrong-gameassembly' -Bare;$entry=Entry-Fixture $f
        Text (Join-Path $f.Game 'GameAssembly.dll') 'incorrect original GameAssembly for the actual installer'
        $before=Tree-State $f;$failure=Expect-Refusal {Validate-Entry $f $entry} 'actual entry wrong GameAssembly'
        Check ($failure.Contains('Unsupported actual game build.')) 'The actual entry failed for a reason other than the exact original-game identity guard.'
        Check ((Tree-State $f) -ceq $before -and -not (Test-Path -LiteralPath $entry.PrepareMarker)) 'Wrong-game readonly refusal changed bytes or reached dependency preparation.'
    }
    Case 'actual-V5-ValidateOnly-foreign-directory-refuses-readonly' {
        $f=New-Fixture 'entry-foreign' -Bare;$entry=Entry-Fixture $f
        Text (Join-Path $f.Game 'MelonLoader/foreign-canary.txt') 'do not adopt this foreign tree'
        $before=Tree-State $f;$failure=Expect-Refusal {Validate-Entry $f $entry} 'actual entry foreign tree'
        Check ($failure.Contains('Foreign loader entry refused: MelonLoader')) 'Readonly foreign probe did not reach its fixed-name directory guard.'
        Check ((Tree-State $f) -ceq $before -and -not (Test-Path -LiteralPath $entry.PrepareMarker)) 'Foreign-tree refusal changed bytes or reached dependency preparation.'
    }
    foreach($item in $inputRows){Check ((Get-InstallHash $item.path) -ceq $item.sha256) 'Frozen package/test input changed during the synthetic matrix.'}
    $report.inputHashesUnchanged=$true;$report.passed=($results.Count -eq $report.expectedCases -and @($results|Where-Object{-not $_.passed}).Count -eq 0)
}catch{$report.failure=$_.Exception.ToString();throw}
finally{
    $report.cases=$results.ToArray();$report.workers=$workers.ToArray();$report.count=$results.Count;$report.passedCount=@($results|Where-Object passed).Count
    $report.artifacts=@(Get-ChildItem -LiteralPath $output -File -Recurse|Where-Object Name -ne 'test-result.json'|ForEach-Object{@{path=$_.FullName;bytes=$_.Length;sha256=Get-InstallHash $_.FullName}})
    Text (Join-Path $output 'test-result.json') ($report|ConvertTo-Json -Depth 24)
}
Write-Output "$($report.passedCount)/$($report.count) V5 synthetic install checks passed; no real installation or native execution: $output"
if(-not $report.passed){exit 1}
