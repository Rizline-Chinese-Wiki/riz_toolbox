#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$GetTimeHelperPath,
    [Parameter(Mandatory)][string]$SourceHelperPath,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateNotNullOrEmpty()]
    [ValidateSet('direct-install','direct-runtime','direct-foreign','direct-worker',
        'direct-source-first','direct-source-first-hold','direct-source-same','direct-source-cross',
        'direct-sdk-foreign','direct-sdk-worker',
        'direct-sdk-withdraw-contract','direct-sdk-worker-clock-contract',
        'progress-foreign','progress-first-hold','player-sdk-normal',
        'stock-stop-replay','stock-stop-resume',
        'direct-sdk-worker-0','direct-sdk-worker-1','direct-sdk-worker-2','direct-sdk-worker-3','direct-sdk-worker-4','direct-sdk-worker-5',
        'legacy-current-0','legacy-current-1','legacy-current-2','legacy-current-3','legacy-current-4',
        'direct-sdk-native-0','direct-sdk-native-1','direct-sdk-native-2','direct-sdk-native-3','direct-sdk-native-4','direct-sdk-native-5',
        'direct-sdk-runtime-0','direct-sdk-runtime-1','direct-sdk-runtime-2','direct-sdk-runtime-3','direct-sdk-runtime-4','direct-sdk-runtime-5')]
    [string[]]$Modes=@('direct-install','direct-runtime')
)
$ErrorActionPreference='Stop'
if (@($Modes | Sort-Object -Unique).Count -ne $Modes.Count) { throw 'Use each mode once so its evidence cannot be overwritten.' }
function Fact([string]$Path) {
    $item=Get-Item -LiteralPath ([IO.Path]::GetFullPath($Path))
    [ordered]@{path=$item.FullName;sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$item.Length}
}
function WriteJson([string]$Path,$Value) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))
}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh evidence directory; earlier failures are retained.'}
$assembly=[IO.Path]::GetFullPath($ManagedAssemblyPath)
$native=@($FixturePath,$GetTimeHelperPath,$SourceHelperPath,$DobbyPath) | ForEach-Object {[IO.Path]::GetFullPath($_)}
if((Fact $native[0]).sha256 -cne '604ebbddb5c7fb19974b1b329a1a5bc6f43d10e6fe6ebfd0e77b6e9e59e186a3'){
    throw 'Only the independently frozen nineteen-member owned fixture may execute.'
}
$files=@($PSCommandPath,$assembly,[IO.Path]::ChangeExtension($assembly,'.pdb'))+$native
foreach($name in @('RizToolbox.Adapter','RizToolbox.Runtime','RizToolbox.Core','RizToolbox.Sdk')){
    $files+=Join-Path (Split-Path -Parent $assembly) ($name+'.dll')
    $files+=Join-Path (Split-Path -Parent $assembly) ($name+'.pdb')
}
$inputs=@($files | ForEach-Object {Fact $_})
$dotnet=(Get-Command dotnet -CommandType Application).Source
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{kind='direct-audio-explicit-owned-runner';passed=$false;inputs=$inputs;rows=@();gameExecuted=$false;productionAdmission=$false}
try {
    foreach($mode in $Modes){
        $command=@($assembly,$mode)+$native
        $row=[ordered]@{mode=$mode;command=@($dotnet)+$command;startedAtUtc=[DateTime]::UtcNow.ToString('o')}
        $stdout=Join-Path $output ($mode+'.stdout.log');$stderr=Join-Path $output ($mode+'.stderr.log')
        # ProcessStartInfo.ArgumentList preserves spaces and raw path characters
        # without shell expansion. No game process or module is a runner target.
        $start=[Diagnostics.ProcessStartInfo]::new($dotnet)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($argument in $command){$start.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::Start($start);$row.pid=$process.Id
        WriteJson (Join-Path $output ($mode+'.launch.json')) $row
        $outTask=$process.StandardOutput.ReadToEndAsync();$errTask=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit();$row.actualExitCode=$process.ExitCode
        [IO.File]::WriteAllText($stdout,$outTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($stderr,$errTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.exit.txt')),[string]$row.actualExitCode)
        $row.finishedAtUtc=[DateTime]::UtcNow.ToString('o');$row.stdout=Fact $stdout;$row.stderr=Fact $stderr
        try{$row.actual=Get-Content -LiteralPath $stdout -Tail 1 | ConvertFrom-Json}catch{$row.actual=$null}
        $row.passed=$row.actualExitCode -eq 0 -and $row.actual.passed -eq $true -and $row.actual.actualPrepare -eq 19 -and $row.actual.actualCommit -eq 19
        $result.rows+=,$row;WriteJson (Join-Path $output ($mode+'.result.json')) $row
        WriteJson (Join-Path $output 'test-result.json') $result
    }
    $result.passed=$result.rows.Count -eq $Modes.Count -and $result.rows.Count -gt 0 -and @($result.rows | Where-Object {!$_.passed}).Count -eq 0
} finally {
    $result.inputsUnchanged=@($inputs | Where-Object {(Fact $_.path).sha256 -cne $_.sha256}).Count -eq 0
    $result.passed=$result.passed -and $result.inputsUnchanged
    $result.processes=$result.rows.Count
    $result.checks=($result.rows | ForEach-Object {$_.actual.checks} | Measure-Object -Sum).Sum
    WriteJson (Join-Path $output 'test-result.json') $result
}
if(!$result.passed){throw 'A direct-audio owned scenario failed; actual evidence was retained.'}
Write-Output "Direct audio: $($result.processes) actual processes, $($result.checks) checks."
