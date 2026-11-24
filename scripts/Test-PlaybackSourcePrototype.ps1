#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,[string[]]$SelectedModes=@())
$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
$build=[IO.Path]::GetFullPath($BuildDirectory);$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new check directory; failed processes must remain intact.'}
$inputs=@('PlaybackSourceFixture.exe','RizToolbox.PlaybackSourcePrototype.dll','RizToolbox.PlaybackStockFixture.dll')|ForEach-Object {Join-Path $build $_}
$inputs+=@([IO.Path]::GetFullPath($DobbyPath),$PSCommandPath,(Join-Path $build 'build-result.json'))
foreach($path in $inputs){if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing check input: $path"}}
$buildResult=Get-Content -LiteralPath (Join-Path $build 'build-result.json') -Raw|ConvertFrom-Json
if(-not $buildResult.passed -or $buildResult.actualExitCode -ne 0){throw 'Only a completed successful native build may be executed.'}
New-Item -ItemType Directory -Path $output|Out-Null
$frozen=Join-Path $output 'inputs';New-Item -ItemType Directory -Path $frozen|Out-Null
foreach($path in $inputs){Copy-Item -LiteralPath $path -Destination (Join-Path $frozen ([IO.Path]::GetFileName($path)))}
$exe=Join-Path $frozen 'PlaybackSourceFixture.exe';$helper=Join-Path $frozen 'RizToolbox.PlaybackSourcePrototype.dll'
$fixture=Join-Path $frozen 'RizToolbox.PlaybackStockFixture.dll';$dobby=Join-Path $frozen ([IO.Path]::GetFileName($DobbyPath))
$manifest=[ordered]@{kind='actual-native-stock-shaped-source-hooks';passed=$false;startedAt=[DateTime]::UtcNow.ToString('o');
    gameCodeExecuted=$false;productionAdmission=$false;inputs=@($inputs|ForEach-Object {[ordered]@{path=$_;sha256=Hash $_}});
    frozenInputs=@(Get-ChildItem -LiteralPath $frozen -File|ForEach-Object {[ordered]@{path=$_.FullName;sha256=Hash $_.FullName}});rows=@()}
$modes=@('normal','bits','nested-aba','original-seh','inline-pause','dormant-withdraw','foreign-thread','observer-fault','journal-overflow','inflight','after-inflight','machine','worker-withdraw','target-specific')
if($SelectedModes.Count -gt 0){$modes=$SelectedModes}
try {
    foreach($mode in $modes){
        $stdout=Join-Path $output "$mode.stdout.log";$stderr=Join-Path $output "$mode.stderr.log"
        & $exe $mode $helper $fixture $dobby 1> $stdout 2> $stderr
        $actualExit=$LASTEXITCODE;[IO.File]::WriteAllText((Join-Path $output "$mode.exit.txt"),[string]$actualExit)
        $parsed=$null;try{$parsed=Get-Content -LiteralPath $stdout -Tail 1|ConvertFrom-Json}catch{}
        $manifest.rows+=@([ordered]@{mode=$mode;actualExitCode=$actualExit;passed=($actualExit -eq 0 -and $parsed.passed -eq $true);result=$parsed})
        Json $manifest (Join-Path $output 'test-result.json')
    }
    $manifest.passed=@($manifest.rows|Where-Object {-not $_.passed}).Count -eq 0
} finally {
    $manifest.inputsUnchanged=@($manifest.inputs|Where-Object {(Hash $_.path) -ne $_.sha256}).Count -eq 0
    $manifest.frozenInputsUnchanged=@($manifest.frozenInputs|Where-Object {(Hash $_.path) -ne $_.sha256}).Count -eq 0
    $manifest.passed=$manifest.passed -and $manifest.inputsUnchanged -and $manifest.frozenInputsUnchanged
    $manifest.processes=$manifest.rows.Count;$manifest.checks=($manifest.rows.result|Measure-Object checks -Sum).Sum
    $manifest.files=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'test-result.json'|ForEach-Object {[ordered]@{path=$_.FullName;bytes=$_.Length;sha256=Hash $_.FullName}})
    $manifest.completedAt=[DateTime]::UtcNow.ToString('o');Json $manifest (Join-Path $output 'test-result.json')
}
if(-not $manifest.passed){throw "Native source prototype checks failed; every actual process retained at $output"}
Write-Output "Native source prototype: $($manifest.processes) processes/$($manifest.checks) checks, all actual exit 0."
