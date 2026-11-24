#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Python = '',
    [ValidateRange(5,60)][int]$TimeoutSeconds=30
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($BinaryDirectory) -or -not [IO.Path]::IsPathFullyQualified($OutputDirectory)){
    throw 'Absolute owned binary and new evidence directories are required.'
}
$binary=(Resolve-Path -LiteralPath $BinaryDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve all previous cold evidence; use a fresh output directory.'}
if($output.StartsWith('D:\',[StringComparison]::OrdinalIgnoreCase)){throw 'This owned fixture must not write into the game drive.'}
$prepare=Join-Path $repo 'docs/validation/foundation-engine-production-20261025/prepare-production-cold.py'
$manifestPath=Join-Path $binary 'production-cold-build-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json -AsHashtable
if($manifest.kind -ne 'owned-engine-production-cold-build' -or $manifest.schemaVersion -ne 2 -or
    $manifest.recordBytes -ne 12448 -or $manifest.snapshotBytes -ne 42512 -or $manifest.publicWinHttpExports -ne 82 -or
    $manifest.productionAdmissionProved -or $manifest.gameExecuted -or $manifest.installed){throw 'Wrong completed owned cold build.'}
New-Item -ItemType Directory -Path $output|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Save([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 50)+"`n",$utf8)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function StreamHash([IO.FileStream]$Stream){$Stream.Position=0;$hasher=[Security.Cryptography.SHA256]::Create();try{[Convert]::ToHexString($hasher.ComputeHash($Stream)).ToLowerInvariant()}finally{$hasher.Dispose();$Stream.Position=0}}
function Verify([string[]]$Arguments,[string]$Prefix){
    & $Python $prepare @Arguments 1>($Prefix+'.stdout.log') 2>($Prefix+'.stderr.log')
    if($LASTEXITCODE -ne 0){throw "Independent raw cold verifier failed: $Prefix"}
}
$selectors=[Collections.Generic.List[object]]::new()
foreach($n in 1..2){$selectors.Add(@{name="normal-$n";expect='normal';directory=$binary;expectedExit=0})}
$copied=[Collections.Generic.List[object]]::new()
foreach($missing in @('RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){
    $label=if($missing.Contains('Bootstrap')){'missing-bootstrap'}else{'missing-kernel'}
    $directory=Join-Path $output ('deploy-'+$label);New-Item -ItemType Directory -Path $directory|Out-Null
    foreach($name in @('RizProductionCold.Host.exe','RizProductionCold.Importer.dll','winhttp.dll',
        'RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','doorstop_config.ini')){
        if($name -eq $missing){continue}
        $source=Join-Path $binary $name;$destination=Join-Path $directory $name
        Copy-Item -LiteralPath $source -Destination $destination
        $copied.Add(@{path=$destination;sha256=(Hash $source)})
    }
    $selectors.Add(@{name=$label;expect='loader-dependency-refusal';directory=$directory;expectedExit=-1073741515;missing=$missing})
}
$held=[Collections.Generic.List[object]]::new();$processes=[Collections.Generic.List[object]]::new();$results=[Collections.Generic.List[object]]::new()
$done=$false;$errorText=$null;$previousErrorMode=$null
try{
    $inputs=@($manifest.files)+@($manifest.sources)+@($manifest.candidateInputs)+@($copied)
    foreach($path in @($manifestPath,$PSCommandPath,$prepare,$Python,(Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'))){$inputs+=@{path=$path;sha256=(Hash $path)}}
    $seen=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($row in $inputs){
        $path=(Resolve-Path -LiteralPath $row.path).Path
        if($seen.ContainsKey($path)){if($seen[$path] -ne $row.sha256){throw 'Conflicting duplicate cold input.'};continue}
        $seen.Add($path,$row.sha256)
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $actual=StreamHash $stream;$held.Add(@{path=$path;sha256=$actual;stream=$stream})
        if($actual -ne $row.sha256){throw "Completed cold input changed: $path"}
    }
    Save (Join-Path $output 'held-inputs.json') @{files=@($held|ForEach-Object{@{path=$_.path;sha256=$_.sha256;writeAndDeleteDenied=$true}})}
    $proof=Join-Path $output 'production-cold-pe-proof.json'
    Verify @('--inspect-build',$binary,'--contract',(Join-Path $binary 'production-cold-contract.json'),'--output',$proof) (Join-Path $output 'inspect')
    # Suppress loader error dialogs inherited by these four exact owned child
    # processes. Restore this PowerShell process's previous error mode below.
    if(-not ('RizProductionColdParent.Native' -as [type])){
        Add-Type -TypeDefinition 'using System.Runtime.InteropServices; namespace RizProductionColdParent { public static class Native { [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode); } }'
    }
    $previousErrorMode=[RizProductionColdParent.Native]::SetErrorMode(0x8003)
    foreach($selector in $selectors){
        $directory=Join-Path $output $selector.name;New-Item -ItemType Directory -Path $directory|Out-Null
        $raw=Join-Path $directory 'case.json'
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $selector.directory 'RizProductionCold.Host.exe'))
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$selector.directory
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add('--output');$start.ArgumentList.Add($raw);$start.Environment['DOORSTOP_ENABLED']='0'
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        $record=@{name=$selector.name;expect=$selector.expect;started=$false;timedOut=$false;pid=$null;exitCode=$null;expectedExit=$selector.expectedExit;
            executable=$start.FileName;workingDirectory=$selector.directory;exactOwnedProcess=$true;missingDependency=$selector.missing}
        $processes.Add($record)
        try{
            if(-not $process.Start()){throw 'Cannot start exact owned cold executable.'}
            $record.started=$true;$record.pid=$process.Id
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            if(-not $process.WaitForExit($TimeoutSeconds*1000)){$record.timedOut=$true;$process.Kill($false);$process.WaitForExit()}
            $record.exitCode=$process.ExitCode
            [IO.File]::WriteAllText((Join-Path $directory 'child.stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8)
            [IO.File]::WriteAllText((Join-Path $directory 'child.stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
            Save (Join-Path $directory 'process.json') $record
            if($record.timedOut -or $record.exitCode -ne $selector.expectedExit){throw "Owned cold process failed: $($selector.name), exit $($record.exitCode)"}
            if($selector.expect -eq 'normal'){
                $resultPath=Join-Path $directory 'result.json'
                Verify @('--case',$raw,'--pe-proof',$proof,'--child-pid',[string]$record.pid,'--output',$resultPath) (Join-Path $directory 'verify')
                $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json -AsHashtable
                if(-not $result.passed -or $result.actualChildPid -ne $record.pid){throw 'Raw proof does not bind the actual child.'}
            }else{
                if((Test-Path -LiteralPath $raw) -or (Test-Path -LiteralPath (Join-Path $directory 'case.snapshot.bin'))){throw 'Missing ordinary dependency unexpectedly reached owned main.'}
                $result=@{kind='owned-production-cold-missing-dependency';passed=$true;actualChildPid=$record.pid;missing=$selector.missing;
                    statusDllNotFound=$true;mainArtifactAbsent=$true;bootstrapExecutionProved=$false;productionAdmissionProved=$false;gameExecuted=$false}
                Save (Join-Path $directory 'result.json') $result
            }
            $results.Add(@{name=$selector.name;process=$record;result=$result})
            Write-Output "Owned production cold $($selector.name): passed, PID $($record.pid), exit $($record.exitCode)."
        }finally{Save (Join-Path $directory 'process.json') $record;$process.Dispose()}
    }
    $done=$true
}catch{$errorText=$_.ToString();throw}
finally{
    if($null -ne $previousErrorMode){[void][RizProductionColdParent.Native]::SetErrorMode($previousErrorMode)}
    $files=@($held|ForEach-Object{@{path=$_.path;beforeSha256=$_.sha256;afterSha256=(StreamHash $_.stream)}})
    $unchanged=@($files|Where-Object{$_.beforeSha256 -ne $_.afterSha256}).Count -eq 0
    foreach($item in $held){$item.stream.Dispose()}
    Save (Join-Path $output 'fixture-result.json') @{kind='owned-engine-production-cold-parent';schemaVersion=2;passed=($done -and $unchanged);
        expectedCases=4;verifiedCases=$results.Count;independentProcesses=$processes.Count;processes=@($processes);caseResults=@($results);
        allInputHashesUnchanged=$unchanged;files=$files;error=$errorText;productionAdmissionProved=$false;fixedGameSeventeenEdgesProved=$false;
        bootstrapDisabledNormalCases=@($results|Where-Object{$_.process.expect -eq 'normal'}).Count;gameExecuted=$false;installed=$false;httpRequestsIssued=$false}
    if($done -and -not $unchanged){throw 'Held owned cold input changed.'}
}
