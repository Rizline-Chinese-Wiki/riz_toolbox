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
$kernel = Join-Path (AbsolutePath $KernelDirectory) 'RizToolbox.EngineNodeKernel.dll'
$output = AbsolutePath $OutputDirectory
$vcvarsPath = AbsolutePath $VcVars
$cpp = Join-Path $repoRoot 'native/RizToolbox.EngineCancellationManagedFixture/CancellationManagedFixture.cpp'
$calls = Join-Path $repoRoot 'native/RizToolbox.EngineCancellationManagedFixture/CancellationCaller.asm'
$sourcePaths = @($cpp, $calls,
    (Join-Path $repoRoot 'native/RizToolbox.EngineWaitFixture/WaitOwned.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineWaitContext.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineSchedulerContext.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineInlineProbe.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/MidContext.h'), $PSCommandPath)
foreach ($path in @($sourcePaths) + @($kernel, $vcvarsPath)) {
    $null = AbsolutePath $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing cancellation managed fixture input: $path" }
}
if (Test-Path -LiteralPath $output) { throw "Use a fresh nonexistent OutputDirectory: $output" }
$inputs = @($sourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = Hash $_ } })
$kernelHash = Hash $kernel
$null = New-Item -ItemType Directory -Path $output
$manifestPath = Join-Path $output 'cancellation-managed-fixture-build.json'
$manifest = [ordered]@{
    schemaVersion = 1; kind = 'engine-cancellation-managed-fixture-build'; startedUtc = [DateTime]::UtcNow.ToString('o')
    buildSucceeded = $false; fixtureExecuted = $false; nativeToClrLedgerVerified = $false
    gameExecuted = $false; installed = $false; productionAdmissionVerified = $false; engineCancellationCompleted = $false
    productionPatchInstallationVerified = $false; outputDirectory = $output; compilerEnvironment = $vcvarsPath
    sources = $inputs; files = @(); buildExitCode = $null; failure = $null
    kernelInput = [ordered]@{ path = $kernel; sha256 = $kernelHash }
}
WriteJson $manifest $manifestPath
$kernelLock = $null
try {
    $kernelLock = [IO.File]::Open($kernel, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $copiedKernel = Join-Path $output 'RizToolbox.EngineNodeKernel.dll'
    [IO.File]::Copy($kernel, $copiedKernel, $false)
    if ((Hash $kernel) -ne $kernelHash -or (Hash $copiedKernel) -ne $kernelHash) { throw 'Kernel changed during its read-locked copy.' }
    $dll = Join-Path $output 'RizToolbox.EngineCancellationManagedFixture.dll'
    $commands = @(
        '@echo off', 'setlocal DisableDelayedExpansion', 'chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\CancellationCaller.obj`" /Fl`"$output\CancellationCaller.lst`" `"$calls`"", 'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\CancellationManagedFixture.obj`" /Fd`"$output\CancellationManagedFixture.compiler.pdb`" `"$cpp`" `"$output\CancellationCaller.obj`" /link /DLL /OUT:`"$dll`" /PDB:`"$output\RizToolbox.EngineCancellationManagedFixture.pdb`" /MAP:`"$output\RizToolbox.EngineCancellationManagedFixture.map`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$dll`" > `"$output\RizToolbox.EngineCancellationManagedFixture.pe.txt`"", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /disasm `"$dll`" > `"$output\RizToolbox.EngineCancellationManagedFixture.disasm.txt`"", 'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    $command = Join-Path $output 'build-engine-cancellation-managed-fixture.cmd'
    [IO.File]::WriteAllLines($command, $commands, [Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $command 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $manifest.buildExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Cancellation managed fixture native build failed: $LASTEXITCODE" }
    foreach ($inputRow in $inputs) { if ((Hash $inputRow.path) -ne $inputRow.sha256) { throw "Source changed during build: $($inputRow.path)" } }
    if ((Hash $kernel) -ne $kernelHash -or (Hash $copiedKernel) -ne $kernelHash) { throw 'Kernel changed during fixture build.' }
    $names = @('RizToolbox.EngineCancellationManagedFixture.dll', 'RizToolbox.EngineNodeKernel.dll',
        'RizToolbox.EngineCancellationManagedFixture.pdb', 'RizToolbox.EngineCancellationManagedFixture.map', 'CancellationCaller.lst',
        'RizToolbox.EngineCancellationManagedFixture.pe.txt', 'RizToolbox.EngineCancellationManagedFixture.disasm.txt',
        'build-engine-cancellation-managed-fixture.cmd', 'build.log')
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
Write-Output "Engine cancellation managed fixture native build: $output"
