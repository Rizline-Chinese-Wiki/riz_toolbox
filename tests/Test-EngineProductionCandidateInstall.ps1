#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'scripts/Install.Common.ps1')
. (Join-Path $repo 'scripts/EngineProductionPackage.Common.ps1')
$candidate=Get-EngineProductionCandidatePackage $CandidateDirectory
$output=Assert-EngineOrdinaryPath $OutputDirectory -MayNotExist
Assert-EnginePackage (-not (Test-Path -LiteralPath $output)) 'use a fresh test output.'
$cursor=Split-Path -Parent $output
while($cursor){Assert-EnginePackage (-not (Test-Path -LiteralPath (Join-Path $cursor 'Rizline.exe'))) 'never place this fixture in a game installation.';$parent=Split-Path -Parent $cursor;if($parent -eq $cursor){break};$cursor=$parent}
New-Item -ItemType Directory -Path $output|Out-Null
$results=[Collections.Generic.List[object]]::new();$script:checks=0
$processEvidence=[Collections.Generic.List[object]]::new()
$global:RizEngineCandidateRemovalFault=$null
function Check([bool]$Value,[string]$Message){++$script:checks;if(-not $Value){throw $Message}}
function Reject([scriptblock]$Action,[string]$Text){$message=$null;try{& $Action|Out-Null}catch{$message=$_.Exception.Message};Check ($message -and $message.Contains($Text)) "Expected '$Text', got '$message'"}
function Write-TestFile([string]$Path,[string]$Text){New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force|Out-Null;[IO.File]::WriteAllText($Path,$Text)}
function Save-Json([string]$Path,$Data){$Data|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $Path -Encoding utf8}
function Case([string]$Name,[scriptblock]$Action){try{& $Action;$results.Add(@{name=$Name;passed=$true});Write-Output "PASS $Name"}catch{$results.Add(@{name=$Name;passed=$false;error=$_.Exception.Message});Write-Output "FAIL $Name : $($_.Exception.Message)"}finally{$global:RizEngineCandidateRemovalFault=$null}}
# Only synthetic installation roots below this new output are used. No native
# binary is launched. The running-game condition is controlled in this fixture.
function Get-Process {[CmdletBinding()]param([string]$Name) if($Name -ne 'Rizline'){throw 'Unexpected process query in installation fixture.'}}
function Remove-Item {
    [CmdletBinding()]param([string]$LiteralPath)
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath
    if($global:RizEngineCandidateRemovalFault -and [IO.Path]::GetFullPath($LiteralPath) -ieq $global:RizEngineCandidateRemovalFault){$global:RizEngineCandidateRemovalFault=$null;throw 'Injected failure immediately after owned layout removal.'}
}
function New-InstallationFixture([string]$Name){
    $root=Join-Path $output $Name;$game=Join-Path $root 'synthetic-game';New-Item -ItemType Directory -Path (Join-Path $root 'scripts'),$game|Out-Null
    foreach($name in @('Install.ps1','Install.Common.ps1','EngineProductionPackage.Common.ps1','Uninstall.ps1')){Copy-Item -LiteralPath (Join-Path $repo "scripts/$name") -Destination (Join-Path $root "scripts/$name")}
    Write-TestFile (Join-Path $root 'scripts/Prepare-Dependencies.ps1') '# Synthetic fixture. No dependency preparation/network/game execution.'
    Write-TestFile (Join-Path $game 'Rizline.exe') 'synthetic fixture executable; never executed'
    Write-TestFile (Join-Path $game 'GameAssembly.dll') 'synthetic original game bytes; never executed'
    @{supportedBuild=@{gameAssemblySha256=(Get-InstallHash (Join-Path $game 'GameAssembly.dll'))}}|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $root 'dependencies.lock.json') -Encoding utf8
    foreach($relative in @('BepInEx/core/dobby.dll','dotnet/runtime.dll')){Write-TestFile (Join-Path $root ".tools/bepinex-789/$relative") 'synthetic dependency; never loaded'}
    Write-TestFile (Join-Path $root '.tools/bepinex-789/doorstop_config.ini') 'target_assembly = fixture'
    $baseRoot=Join-Path $repo 'artifacts/package';$baseManifest=Read-EnginePackageJson (Join-Path $repo 'artifacts/package-manifest.json')
    foreach($row in $baseManifest.files){$destination=Join-Path $root "artifacts/package/$($row.path)";New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force|Out-Null;Copy-Item -LiteralPath (Get-EnginePackageFile $baseRoot $row.path) -Destination $destination}
    Save-Json (Join-Path $root 'artifacts/package-manifest.json') $baseManifest
    return [pscustomobject]@{Root=$root;Game=$game;Manifest=(Join-Path $game 'BepInEx/RizToolbox/installation.json')}
}
function Install-Fixture($Fixture,[switch]$Candidate){if($Candidate){& (Join-Path $Fixture.Root 'scripts/Install.ps1') -GameDirectory $Fixture.Game -CandidateDirectory $script:candidate.Root|Out-Null}else{& (Join-Path $Fixture.Root 'scripts/Install.ps1') -GameDirectory $Fixture.Game|Out-Null}}
function Clone-Candidate([string]$Name){$root=Join-Path $output $Name;New-Item -ItemType Directory -Path $root|Out-Null;foreach($item in Get-ChildItem -LiteralPath $candidate.Root -File -Force -Recurse){$relative=[IO.Path]::GetRelativePath($candidate.Root,$item.FullName);$target=Join-Path $root $relative;New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null;Copy-Item -LiteralPath $item.FullName -Destination $target};return $root}
function Start-TestPowerShell([string]$Script,[string[]]$Arguments,[string]$WorkingDirectory){
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=Join-Path $PSHOME 'pwsh.exe';$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.WorkingDirectory=$WorkingDirectory
    foreach($arg in @('-NoLogo','-NoProfile','-File',$Script)+$Arguments){$info.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info;Check ($process.Start()) 'Test PowerShell worker did not start.';return $process
}
function Save-WorkerEvidence($Process,[string]$Role,[string]$Root){
    $stdout=Join-Path $Root "$Role.stdout.log";$stderr=Join-Path $Root "$Role.stderr.log"
    [IO.File]::WriteAllText($stdout,$Process.StandardOutput.ReadToEnd());[IO.File]::WriteAllText($stderr,$Process.StandardError.ReadToEnd())
    $processEvidence.Add(@{role=$Role;actualProcessId=$Process.Id;actualExitCode=$Process.ExitCode;stdout=$stdout;stderr=$stderr;nativeOrGameExecuted=$false})
}

Case '24 payloads, independent profiles, and closed admission' {
    Check ($candidate.Manifest.files.Count -eq 24) 'Candidate count differs.'
    Check (-not $candidate.Payload.ContainsKey('BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll')) 'Deep Kernel survived packing.'
    Check ($candidate.Payload.ContainsKey('RizToolbox.EngineBootstrap.dll') -and $candidate.Payload.ContainsKey('RizToolbox.EngineNodeKernel.dll')) 'Root dependencies missing.'
    Check (-not $candidate.Manifest.productionAdmissionGranted -and -not $candidate.Relations.Native.executionWindowProved) 'Package granted admission.'
}
Case 'extra payload refused' {$root=Clone-Candidate 'extra';Write-TestFile (Join-Path $root 'package/foreign.dll') 'foreign';Reject {Get-EngineProductionCandidatePackage $root} 'unexpected package file'}
Case 'escaping manifest refused' {$root=Clone-Candidate 'escape';$path=Join-Path $root 'candidate-package-manifest.json';$m=Read-EnginePackageJson $path;$m.files[0].path='../escape.dll';Save-Json $path $m;Reject {Get-EngineProductionCandidatePackage $root} 'unexpected/duplicate file'}
Case 'modified payload refused' {$root=Clone-Candidate 'modified';Write-TestFile (Join-Path $root 'package/RizToolbox.EngineBootstrap.dll') 'modified';Reject {Get-EngineProductionCandidatePackage $root} 'file hash mismatch'}
Case 'self-consistent evidence hash cannot replace compilation relationship' {
    $root=Clone-Candidate 'bad-generated-pin';$path=Join-Path $root 'evidence/managed-build-manifest.json';$m=Read-EnginePackageJson $path;$m.generatedSource.sha256='0'*64;Save-Json $path $m
    $manifestPath=Join-Path $root 'candidate-package-manifest.json';$p=Read-EnginePackageJson $manifestPath;($p.evidence|Where-Object path -eq 'managed-build-manifest.json').sha256=Get-EnginePackageHash $path;Save-Json $manifestPath $p
    Reject {Get-EngineProductionCandidatePackage $root} 'managed build used a different generated profile'
}
Case 'linked package directory refused' {$root=Clone-Candidate 'linked';$outside=Join-Path $output 'linked-owned-target';New-Item -ItemType Directory -Path $outside|Out-Null;New-Item -ItemType Junction -Path (Join-Path $root 'package/linked') -Target $outside|Out-Null;Reject {Get-EngineProductionCandidatePackage $root} 'linked package entry'}
Case 'foreign root dependency refused, including identical bytes' {
    $f=New-InstallationFixture 'foreign';$foreign=Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll';Copy-Item -LiteralPath (Join-Path $candidate.PackageRoot 'RizToolbox.EngineBootstrap.dll') -Destination $foreign;$hash=Get-InstallHash $foreign
    Reject {Install-Fixture $f -Candidate} 'Existing loader installation'
    Check ((Get-InstallHash $foreign) -ceq $hash -and -not (Test-Path -LiteralPath $f.Manifest)) 'Foreign bytes were adopted or overwritten.'
}
Case 'legacy upgrade, candidate repeat, default downgrade and uninstall' {
    $f=New-InstallationFixture 'migration';Install-Fixture $f;$deep=Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll';Check (Test-Path -LiteralPath $deep) 'Default package changed.'
    Install-Fixture $f -Candidate;Check (-not (Test-Path -LiteralPath $deep)) 'Owned deep Kernel was not retired.'
    $m=Read-InstallManifest $f.Game $f.Manifest;foreach($name in @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){Check (@($m.files|Where-Object path -ceq $name).Count -eq 1) 'New root ownership missing.'}
    $count=$m.files.Count;Install-Fixture $f -Candidate;Check ((Read-InstallManifest $f.Game $f.Manifest).files.Count -eq $count) 'Candidate repeat changed ownership.'
    Install-Fixture $f;Check (Test-Path -LiteralPath $deep) 'Default downgrade did not restore deep Kernel.'
    foreach($name in @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){Check (-not (Test-Path -LiteralPath (Join-Path $f.Game $name))) 'Default downgrade retained a root dependency.'}
    Install-Fixture $f -Candidate;& (Join-Path $f.Root 'scripts/Uninstall.ps1') -GameDirectory $f.Game|Out-Null
    Check ((Read-InstallManifest $f.Game $f.Manifest).files.Count -eq 0) 'Candidate uninstall retained unchanged owned files.'
}
Case 'modified obsolete Kernel refuses migration before any replacement' {
    $f=New-InstallationFixture 'edited-old-kernel';Install-Fixture $f;$deep=Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll';Write-TestFile $deep 'user edit';$hash=Get-InstallHash $deep
    Reject {Install-Fixture $f -Candidate} 'Modified/unowned target'
    Check ((Get-InstallHash $deep) -ceq $hash -and -not (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll'))) 'Migration partially replaced files.'
}
Case 'failure after actual obsolete-file removal restores complete prior set' {
    $f=New-InstallationFixture 'rollback';Install-Fixture $f;$before=Read-InstallManifest $f.Game $f.Manifest
    $global:RizEngineCandidateRemovalFault=[IO.Path]::GetFullPath((Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'))
    Reject {Install-Fixture $f -Candidate} 'Injected failure immediately after owned layout removal'
    $after=Read-InstallManifest $f.Game $f.Manifest;Check ($after.files.Count -eq $before.files.Count -and @($after.pending).Count -eq 0) 'Rollback retained mixed ownership.'
    foreach($row in $before.files){Check ((Get-InstallHash (Get-InstallTarget $f.Game $row.path)) -ceq $row.sha256) "Rollback lost old bytes: $($row.path)"}
    Check (-not (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll'))) 'Rollback retained a newly added dependency.'
}
Case 'controlled durable removal journal reload and corrupt-backup refusal' {
    $f=New-InstallationFixture 'journal-reload';Install-Fixture $f;$original=Read-InstallManifest $f.Game $f.Manifest
    $relative='BepInEx\plugins\RizToolbox\RizToolbox.EngineNodeKernel.dll';$deep=Get-InstallTarget $f.Game $relative;$oldHash=Get-InstallHash $deep
    $id=[Guid]::NewGuid().ToString('N');$backupRelative=Get-InstallBackupRelative $id 0
    Write-InstallPayload $f.Game $backupRelative $deep $oldHash $null
    $backup=Get-InstallTarget $f.Game $backupRelative;$backupBytes=[IO.File]::ReadAllBytes($backup)
    $journal=$original|ConvertTo-Json -Depth 10|ConvertFrom-Json
    $journal.pending=@([pscustomobject]@{path=$relative;operation='remove';sha256=$null;previousSha256=$oldHash},
        [pscustomobject]@{path='RizToolbox.EngineBootstrap.dll';operation='write';sha256=$candidate.Payload['RizToolbox.EngineBootstrap.dll'];previousSha256=$null})
    $journal|Add-Member -NotePropertyName transaction -NotePropertyValue ([pscustomobject]@{schemaVersion=2;id=$id})
    Write-InstallManifest $f.Manifest $journal
    Remove-Item -LiteralPath $deep
    Write-InstallPayload $f.Game 'RizToolbox.EngineBootstrap.dll' (Join-Path $candidate.PackageRoot 'RizToolbox.EngineBootstrap.dll') $candidate.Payload['RizToolbox.EngineBootstrap.dll'] $null
    # This is an explicitly constructed durable partial state, not a claim of
    # an actual process crash/restart. Read/restore run through production code.
    $reloaded=Read-InstallManifest $f.Game $f.Manifest
    Write-TestFile $backup 'corrupt backup'
    Reject {Restore-InstallTransaction $f.Game $f.Manifest $reloaded} 'Recovery backup hash mismatch'
    Check (-not (Test-Path -LiteralPath $deep) -and (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll'))) 'Rejected recovery partially changed payloads.'
    Check ((Read-InstallManifest $f.Game $f.Manifest).pending.Count -eq 2) 'Rejected recovery discarded its journal.'
    [IO.File]::WriteAllBytes($backup,$backupBytes)
    $restored=Restore-InstallTransaction $f.Game $f.Manifest (Read-InstallManifest $f.Game $f.Manifest)
    Check ($restored.files.Count -eq $original.files.Count -and @($restored.pending).Count -eq 0) 'Reloaded removal journal did not restore old ownership.'
    Check ((Get-InstallHash $deep) -ceq $oldHash -and -not (Test-Path -LiteralPath (Join-Path $f.Game 'RizToolbox.EngineBootstrap.dll'))) 'Reloaded removal journal did not restore old bytes.'
}
Case 'actual worker termination after removal and separate-process journal recovery' {
    $f=New-InstallationFixture 'terminated-worker';Install-Fixture $f;$before=Read-InstallManifest $f.Game $f.Manifest
    $marker=Join-Path $f.Root 'removed.marker';$stop=[IO.Path]::GetFullPath((Join-Path $f.Game 'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'))
    $workerPath=Join-Path $f.Root 'interrupt-worker.ps1'
    $worker=@'
param([string]$FixtureRoot,[string]$Game,[string]$Candidate,[string]$Stop,[string]$Marker)
$ErrorActionPreference='Stop'
function Get-Process {[CmdletBinding()]param([string]$Name) if($Name -ne 'Rizline'){throw 'Unexpected process query.'}}
function Remove-Item {
    [CmdletBinding()]param([string]$LiteralPath)
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath
    if([IO.Path]::GetFullPath($LiteralPath) -ieq $Stop){
        [IO.File]::WriteAllText($Marker,('Removed owned old Kernel; worker PID '+$PID))
        while($true){Start-Sleep -Milliseconds 100}
    }
}
& (Join-Path $FixtureRoot 'scripts/Install.ps1') -GameDirectory $Game -CandidateDirectory $Candidate
'@
    [IO.File]::WriteAllText($workerPath,$worker)
    $process=Start-TestPowerShell $workerPath @('-FixtureRoot',$f.Root,'-Game',$f.Game,'-Candidate',$candidate.Root,'-Stop',$stop,'-Marker',$marker) $f.Root;$saved=$false
    try{
        $timer=[Diagnostics.Stopwatch]::StartNew()
        while(-not $process.HasExited -and -not (Test-Path -LiteralPath $marker) -and $timer.Elapsed.TotalSeconds -lt 40){Start-Sleep -Milliseconds 25}
        Check ((Test-Path -LiteralPath $marker) -and -not $process.HasExited -and -not (Test-Path -LiteralPath $stop)) 'Worker did not reach the real post-removal boundary.'
        $process.Kill($true);Check ($process.WaitForExit(10000)) 'Interrupted worker did not exit.'
        Save-WorkerEvidence $process 'terminated-install' $f.Root;$saved=$true;Check ($process.ExitCode -ne 0) 'Terminated install incorrectly exited successfully.'
    }finally{if(-not $process.HasExited){$process.Kill($true);$null=$process.WaitForExit(10000)};if(-not $saved -and $process.HasExited){Save-WorkerEvidence $process 'terminated-install' $f.Root};$process.Dispose()}
    $journal=Read-InstallManifest $f.Game $f.Manifest
    Check ($journal.transaction.schemaVersion -eq 2 -and @($journal.pending|Where-Object operation -ceq 'remove').Count -eq 1) 'Actual interrupted removal journal was not preserved.'
    $recoveryPath=Join-Path $f.Root 'recover-worker.ps1';$recoveryReport=Join-Path $f.Root 'recovery-result.json'
    $recovery=@'
param([string]$FixtureRoot,[string]$Game,[string]$Report)
$ErrorActionPreference='Stop'
. (Join-Path $FixtureRoot 'scripts/Install.Common.ps1')
$lock=Enter-InstallLock $Game
try{
    $path=Get-InstallTarget $Game 'BepInEx\RizToolbox\installation.json'
    $manifest=Restore-InstallTransaction $Game $path (Read-InstallManifest $Game $path)
    @{actualProcessId=$PID;pending=@($manifest.pending).Count;files=@($manifest.files).Count;gameExecuted=$false;nativeImagesLoaded=$false}|ConvertTo-Json|Set-Content -LiteralPath $Report -Encoding utf8
}finally{$lock.Dispose()}
'@
    [IO.File]::WriteAllText($recoveryPath,$recovery)
    $process=Start-TestPowerShell $recoveryPath @('-FixtureRoot',$f.Root,'-Game',$f.Game,'-Report',$recoveryReport) $f.Root;$saved=$false
    try{
        Check ($process.WaitForExit(30000)) 'Recovery worker timed out.'
        Save-WorkerEvidence $process 'fresh-recovery' $f.Root;$saved=$true
        Check ($process.ExitCode -eq 0) 'Fresh recovery worker failed.'
        $proof=Read-EnginePackageJson $recoveryReport
        Check ($proof.actualProcessId -eq $process.Id -and $proof.actualProcessId -ne $PID -and $proof.pending -eq 0 -and $proof.files -eq $before.files.Count) 'Fresh-process recovery identity/result differs.'
    }finally{if(-not $process.HasExited){$process.Kill($true);$null=$process.WaitForExit(10000)};if(-not $saved -and $process.HasExited){Save-WorkerEvidence $process 'fresh-recovery' $f.Root};$process.Dispose()}
    $after=Read-InstallManifest $f.Game $f.Manifest;Check ($after.files.Count -eq $before.files.Count -and @($after.pending).Count -eq 0) 'Restart recovery retained mixed ownership.'
    foreach($row in $before.files){Check ((Get-InstallHash (Get-InstallTarget $f.Game $row.path)) -ceq $row.sha256) "Restart recovery lost old bytes: $($row.path)"}
    foreach($name in @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){Check (-not (Test-Path -LiteralPath (Join-Path $f.Game $name))) 'Restart recovery retained a newly added root dependency.'}
}
Case 'new root files preserve edits during uninstall' {
    $f=New-InstallationFixture 'edited-new-kernel';Install-Fixture $f -Candidate;$path=Join-Path $f.Game 'RizToolbox.EngineNodeKernel.dll';Write-TestFile $path 'edited root Kernel';$hash=Get-InstallHash $path
    & (Join-Path $f.Root 'scripts/Uninstall.ps1') -GameDirectory $f.Game|Out-Null
    Check ((Get-InstallHash $path) -ceq $hash) 'Uninstall deleted modified root bytes.'
    Check ((Read-InstallManifest $f.Game $f.Manifest).files.Count -eq 1) 'Uninstall retained incorrect ownership.'
}
$report=@{kind='engine-candidate-package-install-fixture';passed=(@($results|Where-Object{ -not $_.passed }).Count -eq 0);checks=$script:checks;cases=$results.ToArray();processes=$processEvidence.ToArray();syntheticGameOnly=$true;gameExecuted=$false;nativeImagesLoaded=$false;productionAdmissionGranted=$false}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
if(-not $report.passed){throw 'Candidate package/install fixture failed; all output retained.'}
Write-Output "$($results.Count) candidate package/install cases passed; $script:checks assertions; no game/native execution."
