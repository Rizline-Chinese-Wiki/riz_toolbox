#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'An absolute path is required.' }
    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved -match '[\r\n"%&|<>^]') { throw "Unsupported command path characters: $resolved" }
    return $resolved
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Json([object]$Value, [string]$Path) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false)) }
$output = Absolute $OutputDirectory
$vcvarsPath = Absolute $VcVars
$cpp = Join-Path $repo 'native/RizToolbox.PlaybackOwnedFixture/PlaybackOwnedFixture.cpp'
foreach ($inputPath in @($cpp, $PSCommandPath, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing build input: $inputPath" }
}
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must not already exist; failed runs must remain intact: $output" }
$null = New-Item -ItemType Directory -Path $output
$frozenCpp = Join-Path $output 'PlaybackOwnedFixture.cpp'
Copy-Item -LiteralPath $cpp -Destination $frozenCpp
$dll = Join-Path $output 'RizToolbox.PlaybackOwnedFixture.dll'
$obj = Join-Path $output 'PlaybackOwnedFixture.obj'
$pdb = Join-Path $output 'RizToolbox.PlaybackOwnedFixture.pdb'
$batch = Join-Path $output 'build.cmd'
$flags = @('/nologo', '/W4', '/WX', '/O2', '/Z7', '/std:c++17', '/EHsc', '/LD')
$body = @('@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b %errorlevel%',
    "cl $($flags -join ' ') `"$frozenCpp`" /Fo`"$obj`" /link /DEBUG /OUT:`"$dll`" /PDB:`"$pdb`"") -join "`r`n"
[IO.File]::WriteAllText($batch, $body + "`r`n", [Text.UTF8Encoding]::new($false))
$inputs = @($cpp, $PSCommandPath, $vcvarsPath | ForEach-Object { [ordered]@{ path = $_; sha256 = Hash $_ } })
$manifest = [ordered]@{
    kind = 'offline-controlled-playback-owned-fixture'; flavor = $null; startedAt = [DateTime]::UtcNow.ToString('o')
    productionAdmission = $false; realStockHookCoverage = $false; nakedRuntimeWriterCoverage = $false
    gameExecuted = $false; installed = $false; passed = $false; actualExitCode = $null
    sources = $inputs; frozenSourceSha256 = Hash $frozenCpp; flags = $flags; files = @()
}
$manifestPath = Join-Path $output 'build-result.json'
Json $manifest $manifestPath
Push-Location $output
try {
    & $env:ComSpec /d /c $batch 1> (Join-Path $output 'build.stdout.log') 2> (Join-Path $output 'build.stderr.log')
    $manifest.actualExitCode = $LASTEXITCODE
    $manifest.passed = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $dll -PathType Leaf)
    if (-not $manifest.passed) { throw "Owned playback fixture compilation failed with actual exit $LASTEXITCODE; logs retained at $output" }
}
finally {
    Pop-Location
    $manifest.inputsUnchanged = @($inputs | Where-Object { (Hash $_.path) -ne $_.sha256 }).Count -eq 0
    $manifest.frozenSourceUnchanged = (Hash $frozenCpp) -eq $manifest.frozenSourceSha256
    $manifest.passed = $manifest.passed -and $manifest.inputsUnchanged -and $manifest.frozenSourceUnchanged
    $manifest.files = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'build-result.json' | ForEach-Object {
        [ordered]@{ path = $_.Name; bytes = $_.Length; sha256 = Hash $_.FullName }
    })
    $manifest.completedAt = [DateTime]::UtcNow.ToString('o')
    Json $manifest $manifestPath
}
Write-Output $dll
