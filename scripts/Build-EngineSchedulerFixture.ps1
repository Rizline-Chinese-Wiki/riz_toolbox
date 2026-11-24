#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KernelDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
function AbsolutePath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path is required: $Path" }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -match '[\r\n"%&|<>^]') { throw "Unsupported command path characters: $full" }
    $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
$kernelDirectoryPath = AbsolutePath $KernelDirectory
$output = AbsolutePath $OutputDirectory
$vcvarsPath = AbsolutePath $VcVars
$kernelInput = Join-Path $kernelDirectoryPath 'RizToolbox.EngineNodeKernel.dll'
$sourceRoot = Join-Path $repoRoot 'native/RizToolbox.EngineSchedulerFixture'
$cpp = Join-Path $sourceRoot 'SchedulerFixture.cpp'
$asm = Join-Path $sourceRoot 'DrainCaller.asm'
$sourcePaths = @($cpp, $asm, (Join-Path $sourceRoot 'SchedulerOwned.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineSchedulerContext.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/MidContext.h'),
    $PSCommandPath, (Join-Path $PSScriptRoot 'Test-EngineSchedulerFixture.ps1'))
foreach ($path in @($sourcePaths) + @($kernelInput, $vcvarsPath)) {
    $null = AbsolutePath $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing scheduler fixture build input: $path" }
}
if (Test-Path -LiteralPath $output) { throw "Use a fresh nonexistent OutputDirectory: $output" }
$inputs = @($sourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = Hash $_ } })
$kernelHash = Hash $kernelInput
$null = New-Item -ItemType Directory -Path $output
$manifestPath = Join-Path $output 'scheduler-fixture-build.json'
$manifest = [ordered]@{
    schemaVersion = 1; kind = 'engine-scheduler-fixture-build'; startedUtc = [DateTime]::UtcNow.ToString('o')
    buildSucceeded = $false; fixtureExecuted = $false; gameExecuted = $false; installed = $false
    productionAdmissionVerified = $false; engineCancellationCompleted = $false; outputDirectory = $output
    compilerEnvironment = $vcvarsPath; sources = $inputs; files = @(); buildExitCode = $null; failure = $null
    kernelInput = [ordered]@{ path = $kernelInput; sha256 = $kernelHash }
}
WriteJson $manifest $manifestPath
$kernelLock = $null
try {
    $kernelLock = [IO.File]::Open($kernelInput, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $localKernel = Join-Path $output 'RizToolbox.EngineNodeKernel.dll'
    [IO.File]::Copy($kernelInput, $localKernel, $false)
    if ((Hash $localKernel) -ne $kernelHash -or (Hash $kernelInput) -ne $kernelHash) { throw 'Kernel changed while being copied.' }
    $fixture = Join-Path $output 'EngineScheduler.Fixture.exe'
    $commands = @(
        '@echo off', 'setlocal DisableDelayedExpansion', 'chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\DrainCaller.obj`" /Fl`"$output\DrainCaller.lst`" `"$asm`"", 'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\SchedulerFixture.obj`" /Fd`"$output\SchedulerFixture.compiler.pdb`" `"$cpp`" `"$output\DrainCaller.obj`" /link /OUT:`"$fixture`" /PDB:`"$output\EngineScheduler.Fixture.pdb`" /MAP:`"$output\EngineScheduler.Fixture.map`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /exports /unwindinfo `"$fixture`" > `"$output\EngineScheduler.Fixture.pe.txt`"", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /disasm `"$fixture`" > `"$output\EngineScheduler.Fixture.disasm.txt`"", 'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    $commandPath = Join-Path $output 'build-engine-scheduler-fixture.cmd'
    [IO.File]::WriteAllLines($commandPath, $commands, [Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $manifest.buildExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Scheduler fixture build failed: $LASTEXITCODE" }
    foreach ($inputRow in $inputs) {
        if ((Hash $inputRow.path) -ne $inputRow.sha256) { throw "Source changed during build: $($inputRow.path)" }
    }
    if ((Hash $localKernel) -ne $kernelHash -or (Hash $kernelInput) -ne $kernelHash) { throw 'Kernel changed during fixture build.' }
    $names = @('EngineScheduler.Fixture.exe', 'RizToolbox.EngineNodeKernel.dll',
        'EngineScheduler.Fixture.pdb', 'EngineScheduler.Fixture.map', 'DrainCaller.lst',
        'EngineScheduler.Fixture.pe.txt', 'EngineScheduler.Fixture.disasm.txt',
        'build-engine-scheduler-fixture.cmd', 'build.log')
    $manifest.files = @($names | ForEach-Object {
        $path = Join-Path $output $_
        [ordered]@{ name = $_; path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
    })
    $manifest.buildSucceeded = $true
} catch { $manifest.failure = $_.Exception.Message; throw }
finally {
    if ($null -ne $kernelLock) { $kernelLock.Dispose() }
    $manifest.completedUtc = [DateTime]::UtcNow.ToString('o')
    WriteJson $manifest $manifestPath
}
Write-Output "Engine scheduler fixture build: $output"
