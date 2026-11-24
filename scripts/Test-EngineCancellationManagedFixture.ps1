#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$ManagedDirectory,
    [Parameter(Mandatory)][string]$OperationHelper,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,120)][int]$TimeoutSeconds=60,
    [string]$DotNetPath = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $DotNetPath) { $DotNetPath = Get-RizDotnetPath }

$ErrorActionPreference='Stop'
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute path required.'};[IO.Path]::GetFullPath($Path)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 48),[Text.UTF8Encoding]::new($false))}
$binary=Absolute $BinaryDirectory;$managed=Absolute $ManagedDirectory;$operation=Absolute $OperationHelper;$output=Absolute $OutputDirectory;$dotnet=Absolute $DotNetPath
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory.'}
$kernel=Join-Path $binary 'RizToolbox.EngineNodeKernel.dll';$fixture=Join-Path $binary 'RizToolbox.EngineCancellationManagedFixture.dll'
$assembly=Join-Path $managed 'RizToolbox.AudioContent.Tests.dll';$buildManifest=Join-Path $binary 'cancellation-managed-fixture-build.json'
$paths=@($kernel,$fixture,$operation,$assembly,$buildManifest,$dotnet,$PSCommandPath)+@(Get-ChildItem -LiteralPath $managed -File|Where-Object Extension -in @('.dll','.json')|ForEach-Object FullName)
$paths=@($paths|Sort-Object -Unique);foreach($path in $paths){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing input: $path"}}
$build=Get-Content -LiteralPath $buildManifest -Raw|ConvertFrom-Json
if($build.buildSucceeded -ne $true -or $build.buildExitCode -ne 0 -or $build.fixtureExecuted -ne $false){throw 'A successful unexecuted owned fixture build is required.'}
foreach($row in $build.files){if((Hash $row.path) -ne $row.sha256){throw "Native build file drift: $($row.path)"}}
$inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
$locks=[Collections.Generic.List[IDisposable]]::new();$cases=[Collections.Generic.List[object]]::new();$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{schemaVersion=1;kind='owned-engine-cancellation-managed-checks';passed=$false;inputs=$inputs;cases=$cases;gameExecuted=$false;installed=$false;productionEngineCancelled=$false;productionAdmissionGranted=$false}
try{
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($mode in @('normal','missing-source','wrong-generation')){
        $stdout=Join-Path $output ($mode+'.stdout.log');$stderr=Join-Path $output ($mode+'.stderr.log')
        $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$dotnet;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.WorkingDirectory=$output;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add($assembly);$start.ArgumentList.Add('--engine-cancellation-managed');$start.ArgumentList.Add($mode)
        $start.Environment['RIZ_ENGINE_CANCELLATION_KERNEL']=$kernel;$start.Environment['RIZ_ENGINE_CANCELLATION_FIXTURE']=$fixture;$start.Environment['RIZ_ENGINE_CANCELLATION_OPERATION']=$operation
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        $row=[ordered]@{mode=$mode;started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false;passed=$false;issues=@()}
        $outFile=[IO.File]::Open($stdout,'CreateNew','Write','Read');$errFile=[IO.File]::Open($stderr,'CreateNew','Write','Read')
        try{
            if(-not $process.Start()){throw 'Child did not start.'};$row.started=$true;$row.pid=$process.Id
            $copyOut=$process.StandardOutput.BaseStream.CopyToAsync($outFile);$copyErr=$process.StandardError.BaseStream.CopyToAsync($errFile)
            if(-not $process.WaitForExit($TimeoutSeconds*1000)){$row.timedOut=$true;$process.Kill();$row.killed=$true;$null=$process.WaitForExit(10000)}
            if(-not $process.HasExited){throw 'Exact created child has no terminal exit.'};$row.exitCode=$process.ExitCode
            if(-not $copyOut.Wait(5000) -or -not $copyErr.Wait(5000)){throw 'Output pipes did not drain.'}
        }catch{$row.issues+=$_.Exception.Message;if($row.started -and -not $process.HasExited){$process.Kill();$row.killed=$true;$null=$process.WaitForExit(10000)}}
        finally{$outFile.Dispose();$errFile.Dispose();$process.Dispose()}
        try{
            $lines=@([IO.File]::ReadAllLines($stdout)|Where-Object{$_.TrimStart().StartsWith('{')});if($lines.Count -ne 1){throw 'One exact actual child JSON line required.'}
            $child=$lines[0]|ConvertFrom-Json;$row.child=$child
            if($row.exitCode -ne 0 -or $row.timedOut -or $child.passed -ne $true -or $child.pid -ne $row.pid -or $child.mode -ne $mode -or $child.nativeFailures -ne 0 -or
                $child.schemaVersion -ne 1 -or $child.kind -ne 'owned-engine-cancellation-managed-integration' -or
                $child.checks -le 0 -or $child.nativeChecks -le 0 -or @($child.faults).Count -ne 0){throw 'Child terminal status or owned identity failed.'}
            if((Compare-Object @(1,2,4) @($child.sideEvents) -SyncWindow 0) -or
                (Compare-Object @(0,1,1,2,3) @($child.nodeEvents) -SyncWindow 0)){throw 'Actual L side and Node callback order differs from the one same-generation scenario.'}
            $candidates=@($child.candidates)
            if($candidates.Count -ne 1){throw 'One actual Stop/Detach candidate is required.'}
            $candidate=$candidates[0]
            if($candidate.CandidateId -le 0 -or $candidate.RequestId -le 0 -or $candidate.ParentRequestId -ne 0 -or
                $candidate.RootRequestId -ne $candidate.RequestId -or $candidate.RequestSequence -le 0 -or
                $candidate.DetachSequence -le $candidate.RequestSequence -or $candidate.EngineIdentityId -le 0 -or
                $candidate.StartSequence -le 0 -or $candidate.Node -eq 0 -or $candidate.NodeGeneration -le 0 -or
                $candidate.NativeOwner -eq 0 -or $candidate.EnumeratorHandle -eq 0 -or $candidate.NamedMatchUnproven -ne $false){throw 'The exact Stop/Detach request and engine-generation association is incomplete.'}
            $childFiles=@($child.files)
            if($childFiles.Count -ne 3){throw 'Three actual native dependency identities are required.'}
            foreach($path in @($kernel,$fixture,$operation)){
                $matches=@($childFiles|Where-Object{[string]::Equals($_.path,$path,[StringComparison]::OrdinalIgnoreCase)})
                if($matches.Count -ne 1 -or $matches[0].sha256 -ne (Hash $path)){throw 'A loaded native dependency is not the independently frozen fixture file.'}
            }
            foreach($name in @('actualLmnStopVerified','actualInlineMachineBeginEnd','actualStopScopeVerified','exactLedgerTerminalVerified')){if($child.$name -ne $true){throw "Missing actual proof: $name"}}
            foreach($name in @('productionEngineCancelled','productionAdmissionGranted','gameExecuted','installed')){if($child.$name -ne $false){throw "Forbidden production claim: $name"}}
            $expect=$mode -eq 'normal';if($child.ownedCancellationTerminalVerified -ne $expect -or $child.ownedSourceClosureVerified -ne $expect -or $child.rootsReleased -ne $expect){throw 'Owned source generation/missing-source refusal differs.'}
            if($child.stopCaptures -ne 1 -or $child.stopCompletes -ne 1 -or $child.stopDisposes -ne 1 -or $child.sourceCount -ne 5 -or $child.nativeStarts -ne 1 -or $child.nativeDetaches -ne 1 -or $child.nativeCallbacks -ne 1 -or $child.nativeNodesFreed -ne 1 -or $child.nativeWaitsFreed -ne 1){throw 'Native/CLR responsibility counters differ.'}
            $before=@($child.sourceBefore);$after=@($child.sourceAfter);$final=@($child.sourceFinal)
            if($before.Count -ne 5 -or $after.Count -ne 5 -or $final.Count -ne 5){throw 'Five exact source-object records required.'}
            for($index=0;$index -lt 5;++$index){
                $b=$before[$index];$a=$after[$index];$f=$final[$index]
                if($b.Role -ne $index -or $b.Generation -le 0 -or $b.object -eq 0 -or $b.State -ne 0 -or $b.References -ne 2 -or $b.ProducerReferences -ne 1 -or $b.Generation -ne $a.Generation -or $a.Generation -ne $f.Generation -or $b.object -ne $a.object -or $a.object -ne $f.object){throw 'Exact native source object identity drifted.'}
                if($mode -ne 'missing-source' -and ($a.State -ne 2 -or $a.References -ne 1 -or $a.ProducerReferences -ne 0 -or $a.OpenScopes -ne 0 -or $a.WithdrawAttempts -ne 1 -or $a.Dead -le $a.Withdrawn -or $a.Withdrawn -le $a.Generation -or $a.cachedOwner -ne 0)){throw 'Actual native source withdrawal/death did not precede completion.'}
                if($mode -eq 'missing-source' -and ($a.State -ne 0 -or $a.References -ne 2 -or $a.ProducerReferences -ne 1 -or $a.WithdrawAttempts -ne 0 -or $f.State -ne 0 -or $f.References -ne 2)){throw 'The missing-source negative must actually retain all five unwithdrawn producers and roots.'}
                if($expect -and ($f.State -ne 3 -or $f.References -ne 0 -or $f.RootReleaseAttempts -ne 1)){throw 'Each actual source root must be freed exactly once.'}
                if(-not $expect -and $f.RootReleaseAttempts -ne 0){throw 'Rejected source receipt allowed a root release.'}
            }
            $row.passed=$true
        }catch{$row.issues+=$_.Exception.Message}
        $cases.Add($row);WriteJson $row (Join-Path $output ($mode+'.json'))
    }
    foreach($inputRow in $inputs){if((Hash $inputRow.path) -ne $inputRow.sha256){throw "Frozen input drifted: $($inputRow.path)"}}
    $result.passed=@($cases|Where-Object passed -ne $true).Count -eq 0
}finally{foreach($lock in $locks){$lock.Dispose()};WriteJson $result (Join-Path $output 'test-result.json')}
if(-not $result.passed){throw 'Owned cancellation Native-to-CLR integration checks failed.'}
Write-Output "Owned actual L/M/N/Stop checks passed: $output"
