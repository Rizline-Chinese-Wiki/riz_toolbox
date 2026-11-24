#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManagedAssembly,[Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$HelperPath,[Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh broker check output directory.'}
$inputs=@($ManagedAssembly,$FixturePath,$HelperPath,$DobbyPath) | ForEach-Object {[IO.Path]::GetFullPath($_)}
foreach($inputPath in $inputs){if(-not(Test-Path -LiteralPath $inputPath -PathType Leaf)){throw "Missing input: $inputPath"}}
New-Item -ItemType Directory -Path $output | Out-Null
$modes=@('normal','withdraw-inflight','inflight-publish','original-seh-inflight','tail-tamper','patch-tamper','relocation-tamper',
    'relocation-tail-tamper','dobby-original-tamper','unwind-tamper','helper-tamper','dobby-tamper','metadata-tamper','image-tamper',
    'wrong-image-reject','wrong-body-reject','prefix-reject','mi-static-reject','mi-generic-reject','mi-inflated-reject',
    'mi-params-reject','mi-entry-reject','mi-return-reject','fake-engine-reject','precapture-dobby-reject',
    'precapture-helper-reject','precapture-source-tail-reject','precapture-source-consumer-reject','memory-ranges')
$rows=@()
foreach($mode in $modes){
    $log=Join-Path $output "$mode.log"
    & dotnet $inputs[0] $mode $inputs[1] $inputs[2] $inputs[3] 2>&1 | Tee-Object -FilePath $log
    $actualExit=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $output "$mode.exit.txt"),[string]$actualExit)
    $last=Get-Content -LiteralPath $log -Tail 1 | ConvertFrom-Json
    if($actualExit -ne 0 -or $last.passed -ne $true -or $last.mode -ne $mode){throw "Actual broker child failed: $mode, exit=$actualExit"}
    $rows+= $last
}
[ordered]@{kind='actual-owned-playback-broker'; processes=$rows.Count; checks=($rows | Measure-Object checks -Sum).Sum;
    allPassed=$true; actualDobbyCommittedProcesses=@($rows | Where-Object actualDobbyCommitted).Count;
    gameCodeExecuted=$false; productionAdmission=$false; rows=$rows;
    inputs=@($inputs | ForEach-Object {[ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'test-result.json') -Encoding utf8
