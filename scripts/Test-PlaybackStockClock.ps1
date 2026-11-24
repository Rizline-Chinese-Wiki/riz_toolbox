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
if(Test-Path -LiteralPath $out){throw 'Preserve earlier native Clock execution evidence.'}
New-Item -ItemType Directory -Path $out | Out-Null
$source=Split-Path -Parent $PSScriptRoot
$sources=@(Get-ChildItem -LiteralPath (Join-Path $source 'src'),(Join-Path $source 'tests') -Recurse -File |
    Where-Object{$_.Extension -in '.cs','.csproj' -and $_.FullName -notmatch '[\\/](obj|bin)[\\/]'} |
    Sort-Object FullName | ForEach-Object{Fact $_.FullName})
$inputs=@($ManagedAssembly,$FixturePath,$GetTimeHelperPath,$SourceHelperPath,$DobbyPath)|ForEach-Object{[IO.Path]::GetFullPath($_)}
$artifacts=@(Get-ChildItem -LiteralPath (Split-Path -Parent $inputs[0]) -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$result=[ordered]@{kind='stock-final-clock-native-bootstrap-execution';passed=$false;failure=$null;processes=0;checks=0
    sources=$sources;inputs=@($inputs|ForEach-Object{Fact $_});managedArtifacts=$artifacts;runner=Fact $PSCommandPath;rows=@()
    gameExecuted=$false;installed=$false;productionAdmission=$false;completeProvider=$false;crossRunHandoff=$false}
try{
    foreach($mode in @('normal','cancelled-dormant','run-lost-dormant','transition-dormant','external-after-arm','run-loss-after-arm','noop-after-arm','dispose-reader','withdraw-return','rate-rollback')){
        $info=[Diagnostics.ProcessStartInfo]::new((Get-RizDotnetPath))
        $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
        foreach($argument in @($inputs[0],$mode)+$inputs[1..4]){$info.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::new();$process.StartInfo=$info
        if(-not $process.Start()){throw 'Actual Clock test process could not start.'}
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit();$exit=$process.ExitCode;$text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult();$process.Dispose()
        [IO.File]::WriteAllText((Join-Path $out "$mode.stdout.log"),$text,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $out "$mode.stderr.log"),$errors,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $out "$mode.exit.txt"),[string]$exit)
        $lines=@($text -split '\r?\n'|Where-Object{$_.StartsWith('{')})
        $row=[ordered]@{mode=$mode;actualExitCode=$exit;result=$null}
        if($lines.Count -eq 1){$row.result=$lines[0]|ConvertFrom-Json}
        $result.rows+=,$row;$result.processes=$result.rows.Count
        if($exit -ne 0 -or $row.result.passed -ne $true -or $row.result.mode -cne $mode){throw "Actual stock Clock test failed: $mode exit=$exit $errors"}
        $result.checks+=$row.result.checks
        Write-Output "$mode actualExit=$exit checks=$($row.result.checks)"
    }
    foreach($row in $sources+$result.inputs+$artifacts+@($result.runner)){if((Hash $row.path) -cne $row.sha256){throw 'An actual execution source or immutable input changed.'}}
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{[IO.File]::WriteAllText((Join-Path $out 'test-result.json'),($result|ConvertTo-Json -Depth 22),[Text.UTF8Encoding]::new($false))}
