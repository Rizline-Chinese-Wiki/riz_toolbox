#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Python = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$verifier=Join-Path $repoRoot 'docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py'
$binary=(Resolve-Path -LiteralPath $BinaryDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh fixed-publisher evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Save-Json([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 30)+"`n",$utf8)}
function Hash-Stream([IO.FileStream]$Stream){$Stream.Position=0;$hash=[Security.Cryptography.SHA256]::Create();try{[Convert]::ToHexString($hash.ComputeHash($Stream)).ToLowerInvariant()}finally{$hash.Dispose();$Stream.Position=0}}
function Verify([string[]]$Arguments,[string]$Log){& $Python $verifier @Arguments 1> "$Log.stdout.log" 2> "$Log.stderr.log";if($LASTEXITCODE -ne 0){throw "Fixed independent verification failed: $Log"}}
$held=[Collections.Generic.List[object]]::new()
$results=[Collections.Generic.List[object]]::new()
$completed=$false
$errorText=$null
try{
    $manifest=Get-Content -LiteralPath (Join-Path $binary 'fixture-build-manifest.json') -Raw|ConvertFrom-Json -AsHashtable
    foreach($name in @('RizFixedPublisher.dll','RizFixedOwnedTargets.dll','FixedPublisher.Fixture.exe','RizToolbox.JudgeContext.dll')){
        $row=@($manifest.files|Where-Object name -eq $name);if($row.Count -ne 1){throw "Missing exact build input: $name"}
        $path=Join-Path $binary $name
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $item=@{name=$name;path=$path;stream=$stream;sha256=(Hash-Stream $stream)};$held.Add($item)
        if($item.sha256 -ne $row[0].sha256){throw "Fixed binary hash differs: $name"}
    }
    Save-Json (Join-Path $output 'held-inputs.json') @($held|ForEach-Object{@{name=$_.name;path=$_.path;sha256=$_.sha256;writeAndDeleteDenied=$true}})
    $pe=Join-Path $output 'pe-proof.json'
    Verify @('--publisher',(Join-Path $binary 'RizFixedPublisher.dll'),'--targets',(Join-Path $binary 'RizFixedOwnedTargets.dll'),'--helper',(Join-Path $binary 'RizToolbox.JudgeContext.dll'),'--output',$pe) (Join-Path $output 'pe')
    $selectors=@(@{name='normal';stage=0;kind=0})
    foreach($stage in 1..14){foreach($kind in 0..3){$selectors+=@{name="fault-$stage-$kind";stage=$stage;kind=$kind}}}
    foreach($selector in $selectors){
        $directory=Join-Path $output $selector.name;New-Item -ItemType Directory -Path $directory|Out-Null
        $prefix=Join-Path $directory 'case'
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $binary 'FixedPublisher.Fixture.exe'))
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$binary
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add($prefix);$start.ArgumentList.Add([string]$selector.stage);$start.ArgumentList.Add([string]$selector.kind)
        $child=[Diagnostics.Process]::new();$child.StartInfo=$start
        $process=@{stage=$selector.stage;kind=$selector.kind;started=$false;pid=$null;exitCode=$null;timedOut=$false;gameExecuted=$false}
        try{
            if(-not $child.Start()){throw 'Could not start exact owned fixed fixture.'};$process.started=$true;$process.pid=$child.Id
            $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
            if(-not $child.WaitForExit(20000)){$process.timedOut=$true;$child.Kill($false);$child.WaitForExit()}
            $process.exitCode=$child.ExitCode
            [IO.File]::WriteAllText((Join-Path $directory 'child.stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8)
            [IO.File]::WriteAllText((Join-Path $directory 'child.stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
            Save-Json (Join-Path $directory 'process.json') $process
            if($process.timedOut -or $process.exitCode -ne 0){throw "Owned fixed fixture failed: $($selector.name)"}
            $summary=Join-Path $directory 'child.stdout.log'
            $hostResult=Get-Content -LiteralPath $summary -Raw|ConvertFrom-Json -AsHashtable
            if(-not $hostResult.passed -or $hostResult.failures -ne 0 -or $hostResult.controlledUnwindCases -ne 50){throw 'Incomplete actual native fixture result.'}
            Verify @('--receipt',"$prefix.receipt.bin",'--pe-proof',$pe,'--summary',$summary,'--memory',"$prefix.memory.bin",'--output',(Join-Path $directory 'receipt-proof.json')) (Join-Path $directory 'verify-receipt')
            Verify @('--unwind',"$prefix.unwind.jsonl",'--receipt',"$prefix.prepared.bin",'--pe-proof',$pe,'--output',(Join-Path $directory 'unwind-proof.json')) (Join-Path $directory 'verify-unwind')
            if(-not $selector.stage){Verify @('--calls',"$prefix.calls.jsonl",'--summary',$summary,'--output',(Join-Path $directory 'calls-proof.json')) (Join-Path $directory 'verify-calls')}
            $results.Add(@{name=$selector.name;process=$process;host=$hostResult;receipt=(Get-Content (Join-Path $directory 'receipt-proof.json') -Raw|ConvertFrom-Json -AsHashtable);unwind=(Get-Content (Join-Path $directory 'unwind-proof.json') -Raw|ConvertFrom-Json -AsHashtable)})
            Write-Output "Fixed publisher $($selector.name): native and independent receipt/unwind checks passed."
        }finally{$child.Dispose();Save-Json (Join-Path $directory 'process.json') $process}
    }
    $completed=$true
}catch{$errorText=$_.ToString();throw}
finally{
    $files=@($held|ForEach-Object{@{name=$_.name;path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash-Stream $_.stream)}})
    $unchanged=@($files|Where-Object{$_.beforeSha256 -ne $_.afterSha256}).Count -eq 0
    foreach($item in $held){$item.stream.Dispose()}
    Save-Json (Join-Path $output 'fixture-result.json') @{kind='fixed-publisher-parent-evidence';passed=($completed -and $unchanged);verifiedCases=$results.Count;expectedCases=57;caseResults=@($results);files=$files;allInputHashesUnchanged=$unchanged;error=$errorText;
        gameExecuted=$false;productionFirstInstallVerified=$false;loaderStageVerified=$false;engineCancellationCompleted=$false;installed=$false}
}
