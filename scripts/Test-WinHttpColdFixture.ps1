#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BinaryDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Python = '',[ValidateRange(5,60)][int]$TimeoutSeconds=30)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$binary=(Resolve-Path -LiteralPath $BinaryDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
$verifier=Join-Path $repo 'docs/validation/foundation-doorstop-cold-20261025/verify-cold.py'
$prepare=Join-Path $repo 'docs/validation/foundation-doorstop-cold-20261025/prepare-cold-contract.py'
$historical=Join-Path $repo 'docs/validation/foundation-winhttp-forward-20261025/verify-winhttp-forward.py'
$peReader=Join-Path $repo 'docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py'
$manifestPath=Join-Path $binary 'cold-build-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json -AsHashtable
if($manifest.kind -ne 'production-doorstop-cold-build' -or $manifest.publicExports -ne 82 -or -not $manifest.dllEntryProfile){throw 'Wrong completed cold build.'}
if(Test-Path -LiteralPath $output){throw 'Preserve earlier cold execution evidence; use a fresh output directory.'}
New-Item -ItemType Directory -Path $output|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Save([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 60)+"`n",$utf8)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function StreamHash([IO.FileStream]$Stream){$Stream.Position=0;$hasher=[Security.Cryptography.SHA256]::Create();try{[Convert]::ToHexString($hasher.ComputeHash($Stream)).ToLowerInvariant()}finally{$hasher.Dispose();$Stream.Position=0}}
function Verify([string[]]$Arguments,[string]$Prefix){& $Python $verifier @Arguments 1>($Prefix+'.stdout.log') 2>($Prefix+'.stderr.log');if($LASTEXITCODE -ne 0){throw "Independent cold verifier failed: $Prefix"}}
$selectors=[Collections.Generic.List[object]]::new()
foreach($n in 1..3){$selectors.Add(@{name="normal-$n";expect='normal';directory=$binary;expectedExit=0})}
$copied=[Collections.Generic.List[object]]::new()
foreach($shape in @('missing-proxy','legacy-proxy')){
    $directory=Join-Path $output ('deploy-'+$shape)
    New-Item -ItemType Directory -Path $directory|Out-Null
    foreach($name in @('WinHttpCold.Host.exe','WinHttpCold.Importer.dll','doorstop_config.ini')){
        $source=Join-Path $binary $name;$destination=Join-Path $directory $name
        Copy-Item -LiteralPath $source -Destination $destination
        $copied.Add(@{path=$destination;sha256=(Hash $source)})
    }
    if($shape -eq 'legacy-proxy'){
        $destination=Join-Path $directory 'winhttp.dll'
        Copy-Item -LiteralPath $manifest.baselineProxy.path -Destination $destination
        $copied.Add(@{path=$destination;sha256=$manifest.baselineProxy.sha256})
    }
    $selectors.Add(@{name=$shape;expect='identity-refusal';directory=$directory;expectedExit=1})
}
$held=[Collections.Generic.List[object]]::new();$processes=[Collections.Generic.List[object]]::new();$results=[Collections.Generic.List[object]]::new()
$done=$false;$errorText=$null
try{
    $inputs=@($manifest.files)+@($manifest.sources)+@($manifest.baselineProxy)+@($copied)
    foreach($path in @($manifestPath,$PSCommandPath,$verifier,$prepare,$historical,$peReader,$Python,(Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'))){$inputs+=@{path=$path;sha256=(Hash $path)}}
    $seen=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($row in $inputs){
        $path=(Resolve-Path -LiteralPath $row.path).Path
        if($seen.ContainsKey($path)){if($seen[$path] -ne $row.sha256){throw 'Conflicting duplicate cold input.'};continue}
        $seen.Add($path,$row.sha256)
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $actual=StreamHash $stream;$held.Add(@{path=$path;sha256=$actual;stream=$stream})
        if($actual -ne $row.sha256){throw "Cold completed input changed: $path"}
    }
    Save (Join-Path $output 'held-inputs.json') @{files=@($held|ForEach-Object{@{path=$_.path;sha256=$_.sha256;writeAndDeleteDenied=$true}})}
    $proof=Join-Path $output 'pe-proof.json'
    Verify @('--inspect','--binary',$binary,'--output',$proof) (Join-Path $output 'inspect')
    foreach($selector in $selectors){
        $directory=Join-Path $output $selector.name;New-Item -ItemType Directory -Path $directory|Out-Null
        $raw=Join-Path $directory 'case.json'
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $selector.directory 'WinHttpCold.Host.exe'))
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$selector.directory
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.ArgumentList.Add('--output');$start.ArgumentList.Add($raw)
        # The copied ini and explicit environment both keep this self-owned
        # executable on Doorstop's disabled CLR path, before any game content.
        $start.Environment['DOORSTOP_ENABLED']='0'
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        $record=@{name=$selector.name;expect=$selector.expect;started=$false;timedOut=$false;pid=$null;exitCode=$null;expectedExit=$selector.expectedExit;executable=$start.FileName;workingDirectory=$selector.directory;exactOwnedProcess=$true}
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
            if($record.timedOut -or $record.exitCode -ne $selector.expectedExit){throw "Cold process failed: $($selector.name), exit $($record.exitCode)"}
            Verify @('--case',$raw,'--pe-proof',$proof,'--child-pid',[string]$record.pid,'--expect',$selector.expect,'--output',(Join-Path $directory 'result.json')) (Join-Path $directory 'verify')
            $result=Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw|ConvertFrom-Json -AsHashtable
            if(-not $result.passed -or $result.actualChildPid -ne $record.pid){throw 'Cold case did not independently verify the actual child.'}
            $results.Add(@{name=$selector.name;process=$record;result=$result})
            Write-Output "Cold $($selector.name): $($result.checks) independent checks passed."
        }finally{Save (Join-Path $directory 'process.json') $record;$process.Dispose()}
    }
    $done=$true
}catch{$errorText=$_.ToString();throw}
finally{
    $files=@($held|ForEach-Object{@{path=$_.path;beforeSha256=$_.sha256;afterSha256=(StreamHash $_.stream)}})
    $unchanged=@($files|Where-Object{$_.beforeSha256 -ne $_.afterSha256}).Count -eq 0
    foreach($item in $held){$item.stream.Dispose()}
    Save (Join-Path $output 'fixture-result.json') @{kind='production-doorstop-cold-parent';passed=($done -and $unchanged);expectedCases=5;verifiedCases=$results.Count;
        independentProcesses=$processes.Count;processes=@($processes);caseResults=@($results);allInputHashesUnchanged=$unchanged;files=$files;error=$errorText;
        actualProductionDllEntryExecuted=(@($results|Where-Object{$_.process.expect -eq 'normal'}).Count -gt 0);wholeSystemLoaderSafetyVerified=$false;
        gameExecuted=$false;installed=$false;engineV4Issued=$false;fullManagedRegressionRerun=$false}
    if($done -and -not $unchanged){throw 'Held cold input changed.'}
}
