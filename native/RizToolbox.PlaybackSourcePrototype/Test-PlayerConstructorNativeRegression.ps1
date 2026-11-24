#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeBuildDirectory,
    [Parameter(Mandatory)][string]$HistoricalEightRoleBuildDirectory,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh native regression output directory.' }
function Fact([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    [ordered]@{path=$item.FullName; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes=$item.Length}
}
$native = [IO.Path]::GetFullPath($NativeBuildDirectory)
$historical = [IO.Path]::GetFullPath($HistoricalEightRoleBuildDirectory)
$freshManifest = Get-Content -LiteralPath (Join-Path $native 'native-build-result.json') -Raw | ConvertFrom-Json
$oldManifest = Get-Content -LiteralPath (Join-Path $historical 'build-result.json') -Raw | ConvertFrom-Json
if (!$freshManifest.passed -or $freshManifest.actualExitCode -ne 0 -or !$oldManifest.passed -or $oldManifest.actualExitCode -ne 0) {
    throw 'Both actual native builds must have succeeded.'
}
$inputs = @(
    (Join-Path $native 'RizToolbox.PlaybackSourcePrototype.dll'),
    (Join-Path $historical 'PlaybackSourceFixture.exe'),
    (Join-Path $historical 'RizToolbox.PlaybackStockFixture.dll'),
    ([IO.Path]::GetFullPath($DobbyPath)),
    $PSCommandPath
)
$facts = @($inputs | ForEach-Object { Fact $_ })
$null = New-Item -ItemType Directory -Path $output
$frozen = Join-Path $output 'inputs'
$null = New-Item -ItemType Directory -Path $frozen
foreach ($path in $inputs) { Copy-Item -LiteralPath $path -Destination (Join-Path $frozen ([IO.Path]::GetFileName($path))) }
$exe = Join-Path $frozen 'PlaybackSourceFixture.exe'
$helper = Join-Path $frozen 'RizToolbox.PlaybackSourcePrototype.dll'
$fixture = Join-Path $frozen 'RizToolbox.PlaybackStockFixture.dll'
$dobby = Join-Path $frozen ([IO.Path]::GetFileName($DobbyPath))
$modes = @('normal','bits','nested-aba','original-seh','inline-pause','dormant-withdraw','foreign-thread',
    'observer-fault','journal-overflow','inflight','after-inflight','machine','worker-withdraw','target-specific')
$result = [ordered]@{
    kind='current-source-helper-historical-eight-role-regression'; passed=$false
    sourceVersion=4; playerWatchVersion=1; historicalFixtureDoesNotClaimConstructor=$true
    sourceBuild=Fact (Join-Path $native 'native-build-result.json')
    historicalBuild=Fact (Join-Path $historical 'build-result.json')
    inputs=$facts; frozenInputs=@(Get-ChildItem -LiteralPath $frozen -File | ForEach-Object { Fact $_.FullName })
    rows=@(); files=@(); gameExecuted=$false; installed=$false; productionAdmission=$false
}
try {
    foreach ($mode in $modes) {
        $stdout = Join-Path $output "$mode.stdout.log"
        $stderr = Join-Path $output "$mode.stderr.log"
        & $exe $mode $helper $fixture $dobby 1> $stdout 2> $stderr
        $exit = $LASTEXITCODE
        [IO.File]::WriteAllText((Join-Path $output "$mode.exit.txt"), [string]$exit)
        $parsed = $null
        try { $parsed = Get-Content -LiteralPath $stdout -Tail 1 | ConvertFrom-Json } catch {}
        $result.rows += [ordered]@{mode=$mode; actualExitCode=$exit; passed=$exit -eq 0 -and $parsed.passed -eq $true; checks=$parsed.checks; result=$parsed}
    }
    $result.passed = @($result.rows | Where-Object { !$_.passed }).Count -eq 0
} finally {
    $result.inputsUnchanged = @($facts | Where-Object { (Fact $_.path).sha256 -cne $_.sha256 }).Count -eq 0
    $result.frozenInputsUnchanged = @($result.frozenInputs | Where-Object { (Fact $_.path).sha256 -cne $_.sha256 }).Count -eq 0
    $result.processes = $result.rows.Count
    $total = [long]0
    foreach ($row in $result.rows) { $total += [long]$row.checks }
    $result.checks = $total
    $result.passed = $result.passed -and $result.inputsUnchanged -and $result.frozenInputsUnchanged
    $result.files = @(Get-ChildItem -LiteralPath $output -File | Sort-Object FullName | ForEach-Object { Fact $_.FullName })
    [IO.File]::WriteAllText((Join-Path $output 'test-result.json'), ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
if (!$result.passed) { throw 'Current native source helper failed a historical eight-role regression.' }
Write-Output "Historical eight-role regression: $($result.processes) processes, $($result.checks) checks, actual exit 0."
