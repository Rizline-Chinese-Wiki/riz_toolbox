#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManagedAssembly,[Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$GetTimeHelperPath,[Parameter(Mandatory)][string]$SourceHelperPath,
    [Parameter(Mandatory)][string]$DobbyPath,[Parameter(Mandatory)][string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=Hash $p.FullName;bytes=$p.Length}}
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Preserve prior evidence; a fresh occurrence check directory is required.'}
New-Item -ItemType Directory -Path $out | Out-Null
$inputs=@($ManagedAssembly,$FixturePath,$GetTimeHelperPath,$SourceHelperPath,$DobbyPath)|ForEach-Object{[IO.Path]::GetFullPath($_)}
$source=Split-Path -Parent $PSScriptRoot
$sources=@(Get-ChildItem -LiteralPath (Join-Path $source 'src'),(Join-Path $source 'tests') -Recurse -File|Where-Object{$_.Extension -in '.cs','.csproj' -and $_.FullName -notmatch '[\\/](obj|bin)[\\/]'}|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$artifacts=@(Get-ChildItem -LiteralPath (Split-Path -Parent $inputs[0]) -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$result=[ordered]@{schemaVersion=1;kind='actual-stock-rooted-native-api-checks';passed=$false;failure=$null;processes=0;checks=0;
    gameExecuted=$false;providerIssued=$false;productionAdmission=$false;writerReservationIssued=$true;installed=$false;hostRunAssociationIssued=$true;completeNativeSourceCoverage=$false;
    sources=$sources;inputs=@($inputs|ForEach-Object{Fact $_});managedArtifacts=$artifacts;runner=Fact $PSCommandPath;rows=@()}
try{
    foreach($mode in @('normal','dispose-inflight','dsp-regression','external-aba','readback-mismatch','graph-replacement','getter-writer-race',
        'run-observation-gap','run-availability-gap','run-scene-aba','run-controller-aba','run-over','run-cancel','run-during-read','run-inflight')){
        $start=[Diagnostics.ProcessStartInfo]::new((Get-RizDotnetPath));$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($arg in @($inputs[0],$mode)+$inputs[1..4]){$start.ArgumentList.Add($arg)}
        $p=[Diagnostics.Process]::new();$p.StartInfo=$start;if(-not $p.Start()){throw 'Could not start actual occurrence fixture.'}
        $stdout=$p.StandardOutput.ReadToEndAsync();$stderr=$p.StandardError.ReadToEndAsync()
        $p.WaitForExit()
        $exit=$p.ExitCode;$text=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult();$p.Dispose()
        [IO.File]::WriteAllText((Join-Path $out "$mode.stdout.log"),$text,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $out "$mode.stderr.log"),$errorText,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $out "$mode.exit.txt"),[string]$exit)
        $lines=@($text -split '\r?\n'|Where-Object{$_.StartsWith('{')})
        $row=[ordered]@{mode=$mode;actualExitCode=$exit;result=$null};if($lines.Count -eq 1){$row.result=$lines[0]|ConvertFrom-Json};$result.rows+=@($row)
        $result.processes=$result.rows.Count
        if($exit -ne 0 -or $row.result.passed -ne $true -or $row.result.mode -cne $mode){throw "Actual stock occurrence failed: $mode exit=$exit $errorText"}
        $result.checks+=$row.result.checks
        Write-Output "$mode actualExit=$exit checks=$($row.result.checks)"
    }
    foreach($row in $sources+$result.inputs+$artifacts+@($result.runner)){if((Hash $row.path) -cne $row.sha256){throw 'An exact source or immutable input changed during occurrence checks.'}}
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{[IO.File]::WriteAllText((Join-Path $out 'test-result.json'),($result|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
