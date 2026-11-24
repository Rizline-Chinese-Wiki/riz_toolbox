#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManagedAssembly,[Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$GetTimeHelperPath,[Parameter(Mandatory)][string]$SourceHelperPath,
    [Parameter(Mandatory)][string]$DobbyPath,[Parameter(Mandatory)][string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
function Fact([string]$Path){$item=Get-Item -LiteralPath $Path;[ordered]@{path=$item.FullName;sha256=Hash $item.FullName;bytes=$item.Length}}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh common source-group check directory.'}
$inputs=@($ManagedAssembly,$FixturePath,$GetTimeHelperPath,$SourceHelperPath,$DobbyPath) | ForEach-Object {[IO.Path]::GetFullPath($_)}
foreach($inputPath in $inputs){if(-not(Test-Path -LiteralPath $inputPath -PathType Leaf)){throw "Missing input: $inputPath"}}
New-Item -ItemType Directory -Path $output | Out-Null
$roles=@('pitch','pause','replay','resume','schedule','clip','progress','audiopause')
$modes=@('precapture-pitch-uw','precapture-replay-chain-uw','precapture-gettime-uw',
    'normal-nine','adopt-nine','stream-nine','withdraw-blocked-nine','native-thread-fault-nine','callback-before-nine','callback-after-nine','reader-fault-nine','seh-gettime')
foreach($role in $roles){$modes+=@("seh-$role","withdraw-$role")}
foreach($role in @('gettime')+$roles){foreach($span in @('patch','tail','original','dobby','unwind','metadata')){$modes+="tamper-$role-$span"}}
$managedDirectory=Split-Path -Parent $inputs[0]
$managedFiles=@(Get-ChildItem -LiteralPath $managedDirectory -File -Recurse|Sort-Object FullName|ForEach-Object {Fact $_.FullName})
$result=[ordered]@{kind='actual-owned-eleven-member-playback-source-group';processes=0;checks=0;allPassed=$false;
    actualDobbyPrepareCalls=0;actualDobbyCommitCalls=0;actualPrivateBrokerAdoptionProcesses=0;
    gameCodeExecuted=$false;productionAdmission=$false;installed=$false;failure=$null;rows=@();
    inputs=@(@($PSCommandPath)+$inputs|ForEach-Object {Fact $_});managedArtifacts=$managedFiles;artifacts=@()}
try{
    foreach($mode in $modes){
        $stdoutPath=Join-Path $output "$mode.stdout.log"
        $stderrPath=Join-Path $output "$mode.stderr.log"
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=(Get-RizDotnetPath);$start.UseShellExecute=$false
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.CreateNoWindow=$true
        foreach($argument in @($inputs[0],$mode,$inputs[1],$inputs[2],$inputs[3],$inputs[4])){$start.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::Start($start)
        try{
            $stdoutTask=$process.StandardOutput.ReadToEndAsync();$stderrTask=$process.StandardError.ReadToEndAsync()
            $process.WaitForExit();$actualExit=$process.ExitCode
            $stdout=$stdoutTask.GetAwaiter().GetResult();$stderr=$stderrTask.GetAwaiter().GetResult()
        }finally{$process.Dispose()}
        [IO.File]::WriteAllText($stdoutPath,$stdout,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($stderrPath,$stderr,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output "$mode.exit.txt"),[string]$actualExit)
        $row=[ordered]@{mode=$mode;actualExitCode=$actualExit;passed=$false;checks=0;
            stdout=Fact $stdoutPath;stderr=Fact $stderrPath}
        $result.rows+=,$row;$result.processes=$result.rows.Count
        $line=@($stdout -split '\r?\n'|Where-Object {$_ -ne ''})|Select-Object -Last 1
        if($line){
            $actual=$line|ConvertFrom-Json -AsHashtable
            foreach($key in $actual.Keys){$row[$key]=$actual[$key]}
        }
        if($actualExit -ne 0 -or $row.passed -ne $true -or $row.mode -cne $mode){throw "Actual source-group child failed: $mode, exit=$actualExit"}
        if($mode.StartsWith('precapture-',[StringComparison]::Ordinal)){
            if($row.preCaptureRejected -ne $true -or $row.actualGroupArmed -ne $false -or $row.actualDobbyCommitCount -ne 0 -or
                $row.actualDobbyPrepareCount -ne 0 -or $row.nativeEntriesInvoked -ne $false){throw "Unexpected actual pre-capture refusal: $mode"}
        }elseif($row.actualDobbyPrepareCount -ne 11 -or $row.actualDobbyCommitCount -ne 11 -or
            $row.physicalMemberCount -ne 11 -or $row.sourceMemberCount -ne 10 -or $row.sharedSourceHubCount -ne 1){throw "Unexpected actual source-group proof: $mode"}
        if($row.gameCodeExecuted -ne $false -or $row.productionAdmission -ne $false){throw "Unexpected source-group execution or production authority: $mode"}
        $result.checks+=$row.checks;$result.actualDobbyPrepareCalls+=$row.actualDobbyPrepareCount
        $result.actualDobbyCommitCalls+=$row.actualDobbyCommitCount
        if($row.adoptedActualBrokerOwner){++$result.actualPrivateBrokerAdoptionProcesses}
        Write-Output "$mode`: exit=$actualExit, $($row.checks) checks."
    }
    foreach($row in @($result.inputs)+@($result.managedArtifacts)){
        if((Hash $row.path) -ne $row.sha256){throw "A fixed source-group input changed: $($row.path)"}
    }
    $result.allPassed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    $result.artifacts=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'test-result.json'|Sort-Object Name|ForEach-Object {Fact $_.FullName})
    Json $result (Join-Path $output 'test-result.json')
}
Write-Output "Eleven-member playback source group: $($result.processes) processes/$($result.checks) checks."
