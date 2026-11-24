#requires -Version 7.2
[CmdletBinding(DefaultParameterSetName = 'Directory')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Directory')][string]$KernelDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Dll')][string]$KernelDll,
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
    return $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
$output = AbsolutePath $OutputDirectory
$vcvarsPath = AbsolutePath $VcVars
$kernelInput = if ($PSCmdlet.ParameterSetName -eq 'Directory') {
    Join-Path (AbsolutePath $KernelDirectory) 'RizToolbox.EngineNodeKernel.dll'
} else { AbsolutePath $KernelDll }
$cpp = Join-Path $repoRoot 'native/RizToolbox.EngineInlineManagedFixture/InlineManagedFixture.cpp'
# Compile the exact already inspected source into the owned fixture DLL. No
# copying of the old EXE or its GetModuleHandle(nullptr) assumptions is used.
$asm = Join-Path $repoRoot 'native/RizToolbox.EngineInlineFixture/InlineCaller.asm'
$sourcePaths = @($cpp, $asm,
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineInlineProbe.h'),
    (Join-Path $repoRoot 'native/RizToolbox.JudgeContext/MidContext.h'),
    (Join-Path $repoRoot 'tests/RizToolbox.AudioContent.Tests/NativeEngineInlineManagedIntegration.cs'), $PSCommandPath)
foreach ($path in @($sourcePaths) + @($kernelInput, $vcvarsPath)) {
    $null = AbsolutePath $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build input: $path" }
}
if (Test-Path -LiteralPath $output) { throw "OutputDirectory must be fresh and nonexistent: $output" }
$inputs = @($sourcePaths | ForEach-Object { [ordered]@{ path = $_; sha256 = Hash $_ } })
$kernelHash = Hash $kernelInput
$null = New-Item -ItemType Directory -Path $output
$manifestPath = Join-Path $output 'inline-managed-fixture-build.json'
$manifest = [ordered]@{
    schemaVersion = 1; kind = 'engine-inline-managed-fixture-build'; startedUtc = [DateTime]::UtcNow.ToString('o')
    buildSucceeded = $false; fixtureExecuted = $false; nativeToClrLedgerVerified = $false
    gameExecuted = $false; installed = $false; productionAdmissionVerified = $false; engineCancellationCompleted = $false
    outputDirectory = $output; compilerEnvironment = $vcvarsPath
    kernelInput = [ordered]@{ path = $kernelInput; sha256 = $kernelHash }
    sources = $inputs; files = @(); buildExitCode = $null; failure = $null
}
WriteJson $manifest $manifestPath
$kernelLock = $null
try {
    $kernelLock = [IO.File]::Open($kernelInput, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $localKernel = Join-Path $output 'RizToolbox.EngineNodeKernel.dll'
    [IO.File]::Copy($kernelInput, $localKernel, $false)
    if ((Hash $localKernel) -ne $kernelHash -or (Hash $kernelInput) -ne $kernelHash) { throw 'Kernel changed while copying.' }
    $fixture = Join-Path $output 'RizEngineInline.ManagedFixture.dll'
    $commands = @(
        '@echo off', 'setlocal DisableDelayedExpansion', 'chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\InlineCaller.obj`" /Fl`"$output\InlineCaller.lst`" `"$asm`"", 'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\InlineManagedFixture.obj`" /Fd`"$output\InlineManagedFixture.compiler.pdb`" `"$cpp`" `"$output\InlineCaller.obj`" /link /DLL /OUT:`"$fixture`" /PDB:`"$output\RizEngineInline.ManagedFixture.pdb`" /MAP:`"$output\RizEngineInline.ManagedFixture.map`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$fixture`" > `"$output\RizEngineInline.ManagedFixture.pe.txt`"", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /disasm `"$fixture`" > `"$output\RizEngineInline.ManagedFixture.disasm.txt`"", 'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /exports /unwindinfo `"$localKernel`" > `"$output\RizToolbox.EngineNodeKernel.pe.txt`"", 'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    $commandPath = Join-Path $output 'build-engine-inline-managed-fixture.cmd'
    [IO.File]::WriteAllLines($commandPath, $commands, [Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $manifest.buildExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Managed fixture native build failed with exit code $LASTEXITCODE; artifacts retained." }
    foreach ($row in $inputs) { if ((Hash $row.path) -ne $row.sha256) { throw "Source changed during build: $($row.path)" } }
    if ((Hash $kernelInput) -ne $kernelHash -or (Hash $localKernel) -ne $kernelHash) { throw 'Kernel hash changed during build.' }
    $manifest.files = @(@('RizEngineInline.ManagedFixture.dll', 'RizToolbox.EngineNodeKernel.dll',
        'RizEngineInline.ManagedFixture.pdb', 'RizEngineInline.ManagedFixture.map', 'InlineCaller.lst',
        'RizEngineInline.ManagedFixture.pe.txt', 'RizEngineInline.ManagedFixture.disasm.txt', 'RizToolbox.EngineNodeKernel.pe.txt',
        'build-engine-inline-managed-fixture.cmd', 'build.log') | ForEach-Object {
            $path = Join-Path $output $_
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build output: $path" }
            [ordered]@{ name = $_; path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
        })
    $manifest.buildSucceeded = $true
} catch { $manifest.failure = $_.Exception.Message; throw }
finally {
    if ($null -ne $kernelLock) { $kernelLock.Dispose() }
    $manifest.completedUtc = [DateTime]::UtcNow.ToString('o'); WriteJson $manifest $manifestPath
}
Write-Output "Engine inline managed fixture build: $output"
