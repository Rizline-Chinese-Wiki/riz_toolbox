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
$repoRoot=Split-Path -Parent $PSScriptRoot
$binary=(Resolve-Path -LiteralPath $BinaryDirectory).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
$verifier=Join-Path $repoRoot 'docs/validation/foundation-winhttp-forward-20261025/verify-winhttp-forward.py'
$historicalVerifier=Join-Path $repoRoot 'docs/validation/foundation-early-fixed-20261025/verify-early-fixed.py'
$pythonPath=(Resolve-Path -LiteralPath $Python).Path
$manifestPath=(Resolve-Path -LiteralPath (Join-Path $binary 'fixture-build-manifest.json')).Path
$systemPath=Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'
if(Test-Path -LiteralPath $output){throw 'Use a fresh WinHTTP evidence output directory.'}
New-Item -ItemType Directory -Path $output|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Save-Json([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 45)+"`n",$utf8)}
function Hash-Stream([IO.FileStream]$Stream){$Stream.Position=0;$hash=[Security.Cryptography.SHA256]::Create();try{[Convert]::ToHexString($hash.ComputeHash($Stream)).ToLowerInvariant()}finally{$hash.Dispose();$Stream.Position=0}}
function Verify([string[]]$Arguments,[string]$Prefix){& $pythonPath $verifier @Arguments 1>($Prefix+'.stdout.log') 2>($Prefix+'.stderr.log');if($LASTEXITCODE -ne 0){throw "WinHTTP independent verifier failed: $Prefix"}}
$held=[Collections.Generic.List[object]]::new()
$records=[Collections.Generic.List[object]]::new()
$results=[Collections.Generic.List[object]]::new()
$done=$false
$errorText=$null
try{
    $manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json -AsHashtable
    if($manifest.kind -ne 'fixed-winhttp-forward-build' -or $manifest.files.Count -ne 4){throw 'Unexpected completed WinHTTP build manifest.'}
    $expected=@(
        @{path=(Join-Path $binary 'production/RizToolbox.WinHttpForward.dll');profile='production';role='forward'},
        @{path=(Join-Path $binary 'fixture/RizToolbox.WinHttpForward.dll');profile='fixture';role='forward'},
        @{path=(Join-Path $binary 'RizWinHttpOwnedImporter.dll');profile='shared';role='importer'},
        @{path=(Join-Path $binary 'WinHttpForward.Fixture.exe');profile='shared';role='host'})
    $rows=@()
    foreach($item in $expected){
        $found=@($manifest.files|Where-Object{[IO.Path]::GetFullPath($_.path) -eq [IO.Path]::GetFullPath($item.path)})
        if($found.Count -ne 1 -or $found[0].profile -ne $item.profile -or $found[0].role -ne $item.role){throw 'Ambiguous exact built binary identity.'}
        $rows+=@{path=$item.path;sha256=$found[0].sha256;role=$item.role;profile=$item.profile}
    }
    $rows+=@($manifest.sources)
    foreach($path in @($PSCommandPath,$verifier,$historicalVerifier,$pythonPath,$manifestPath,$systemPath)){
        $rows+=@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $seen=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($row in $rows){
        if(-not [IO.Path]::IsPathFullyQualified($row.path) -or $row.sha256 -notmatch '^[0-9a-fA-F]{64}$'){throw 'Invalid absolute hashed WinHTTP input.'}
        $path=(Resolve-Path -LiteralPath $row.path).Path
        if($seen.ContainsKey($path)){if($seen[$path] -ne $row.sha256){throw 'Duplicate input has conflicting hashes.'};continue}
        $seen.Add($path,$row.sha256)
        $stream=[IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $item=@{path=$path;sha256=(Hash-Stream $stream);stream=$stream}
        $held.Add($item)
        if($item.sha256 -ne $row.sha256.ToLowerInvariant()){throw "WinHTTP input differs from its completed build: $path"}
    }
    Save-Json (Join-Path $output 'held-inputs.json') @{files=@($held|ForEach-Object{@{path=$_.path;sha256=$_.sha256;writeAndDeleteDenied=$true}})}
    $peProof=Join-Path $output 'pe-proof.json'
    Verify @('--inspect','--forward',(Join-Path $binary 'production/RizToolbox.WinHttpForward.dll'),
        '--fixture-forward',(Join-Path $binary 'fixture/RizToolbox.WinHttpForward.dll'),'--importer',(Join-Path $binary 'RizWinHttpOwnedImporter.dll'),
        '--host',(Join-Path $binary 'WinHttpForward.Fixture.exe'),'--output',$peProof) (Join-Path $output 'inspect')
    $selectors=[Collections.Generic.List[object]]::new()
    foreach($profile in @('production','fixture')){$selectors.Add(@{name=$profile+'-normal';profile=$profile;stage=0;index=0})}
    foreach($stage in 1..10){
        $indices=if($stage -in @(5,6,10)){0..3}else{@(0)}
        foreach($index in $indices){$selectors.Add(@{name="fault-$stage-$index";profile='fixture';stage=$stage;index=$index})}
    }
    foreach($selector in $selectors){
        $directory=Join-Path $output $selector.name
        New-Item -ItemType Directory -Path $directory|Out-Null
        $raw=Join-Path $directory 'case.json'
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $binary 'WinHttpForward.Fixture.exe'))
        $start.WorkingDirectory=$binary;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($argument in @('--forward',(Join-Path $binary ($selector.profile+'/RizToolbox.WinHttpForward.dll')),
            '--output',$raw,'--fault',[string]$selector.stage,'--fault-index',[string]$selector.index)){$start.ArgumentList.Add($argument)}
        if($selector.profile -eq 'fixture'){$start.ArgumentList.Add('--fixture')}
        if($selector.stage -eq 0){$start.ArgumentList.Add('--importer');$start.ArgumentList.Add((Join-Path $binary 'RizWinHttpOwnedImporter.dll'))}
        $child=[Diagnostics.Process]::new();$child.StartInfo=$start
        $record=@{name=$selector.name;profile=$selector.profile;stage=$selector.stage;index=$selector.index;started=$false;timedOut=$false;pid=$null;exitCode=$null;exactOwnedProcess=$true}
        $records.Add($record)
        try{
            if(-not $child.Start()){throw 'Could not start the exact owned WinHTTP host.'}
            $record.started=$true;$record.pid=$child.Id
            $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
            if(-not $child.WaitForExit($TimeoutSeconds*1000)){$record.timedOut=$true;$child.Kill($false);$child.WaitForExit()}
            $record.exitCode=$child.ExitCode
            [IO.File]::WriteAllText((Join-Path $directory 'child.stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8)
            [IO.File]::WriteAllText((Join-Path $directory 'child.stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
            Save-Json (Join-Path $directory 'process.json') $record
            if($record.timedOut -or $record.exitCode -ne 0){throw "Owned WinHTTP host failed: $($selector.name)"}
            Verify @('--case',$raw,'--pe-proof',$peProof,'--child-pid',[string]$record.pid,'--profile',$selector.profile,
                '--fault-stage',[string]$selector.stage,'--fault-index',[string]$selector.index,'--output',(Join-Path $directory 'result.json')) (Join-Path $directory 'verify')
            $result=Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw|ConvertFrom-Json -AsHashtable
            if($result.passed -ne $true -or $result.checks -le 0 -or $result.actualChildPid -ne $record.pid -or $result.profile -ne $selector.profile -or
                $result.faultStage -ne $selector.stage -or $result.faultIndex -ne $selector.index){throw 'Incomplete or mismatched independent WinHTTP proof.'}
            $results.Add(@{name=$selector.name;process=$record;result=$result})
            Write-Output "WinHTTP $($selector.name): $($result.checks) independent checks passed."
        }finally{$child.Dispose();Save-Json (Join-Path $directory 'process.json') $record}
    }
    $done=$true
}catch{$errorText=$_.ToString();throw}
finally{
    $files=@($held|ForEach-Object{@{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash-Stream $_.stream)}})
    $unchanged=@($files|Where-Object{$_.beforeSha256 -ne $_.afterSha256}).Count -eq 0
    foreach($item in $held){$item.stream.Dispose()}
    Save-Json (Join-Path $output 'fixture-result.json') @{kind='fixed-winhttp-forward-parent-evidence';passed=($done -and $unchanged);expectedCases=21;verifiedCases=$results.Count;independentProcesses=$records.Count;
        processes=@($records);caseResults=@($results);files=$files;allInputHashesUnchanged=$unchanged;error=$errorText;rootSoleBuilder=$true;
        systemWinHttpCodeExecutionVerified=(@($results|Where-Object{$_.process.stage -eq 0}).Count -gt 0);systemExecutionMayHaveOccurred=($records.Count -gt 0);
        gameCodeExecuted=$false;networkRequestsIssuedByFixture=$false;networkTraceCaptured=$false;installed=$false;productionLoaderStageVerified=$false;engineV4Issued=$false}
    if($done -and -not $unchanged){throw 'A held WinHTTP input changed.'}
}
